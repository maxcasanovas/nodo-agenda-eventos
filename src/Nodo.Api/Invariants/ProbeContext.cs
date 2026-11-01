using MediatR;
using Microsoft.EntityFrameworkCore;
using Nodo.Api.Infrastructure;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Infrastructure.Persistence;
using Nodo.External.Simulated.Spaces;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Infrastructure.Persistence;
using Nodo.Quotes.Application;
using Nodo.Quotes.Infrastructure.Persistence;

namespace Nodo.Api.Invariants;

/// <summary>
/// What a probe reports. <see cref="Attempted"/> is false for the rules kept "by design" (the operation does not exist);
/// <see cref="Attempt"/> says in Spanish what was tried.
/// </summary>
public sealed record ProbeResult(
    string InvariantId, bool Attempted, string Attempt, bool Rejected, string? Code, string Message, int StatusCode);

internal sealed record ProbeOutcome(bool Attempted, string Attempt, bool Rejected, string? Code, string Message, int StatusCode);

/// <summary>
/// The tools a probe uses: real use cases through MediatR and the module APIs, all inside one transaction that the runner
/// rolls back. It also builds the ephemeral fixtures (quotes, requests, payments) a probe needs.
/// </summary>
internal sealed class ProbeContext(IServiceProvider services)
{
    /// <summary>A customer nobody knows: no membership, so prices carry no discount and are easy to predict (4 h = 1.800 Bs).</summary>
    public static readonly Guid Customer = Guid.Parse("99999999-0000-0000-0000-000000000001");

    public const string Actor = "Sonda de invariantes";

    private readonly HashSet<Guid> _delivered = [];

    public IMediator Mediator => services.GetRequiredService<IMediator>();

    /// <summary>Moves time forward for this probe only: the demo clock and every other request are untouched.</summary>
    public ClockOffset ClockOffset => services.GetRequiredService<ClockOffset>();

    public DateOnly Today => services.GetRequiredService<IClock>().TodayLocal;

    public Guid HallId => Guid.Parse(services.GetRequiredService<SpacesContext>().Venues[0].VenueUid);

    public T Service<T>() where T : notnull => services.GetRequiredService<T>();

    /// <summary>A day far from the demo data and inside the 3-180 day booking window.</summary>
    public DateOnly FarDay(int plusDays = 0) => Today.AddDays(100 + plusDays);

    // ---- What happened -------------------------------------------------------------------------------------------

    /// <summary>Runs the violating call. A rejection is reported exactly as the API would answer it.</summary>
    public async Task<ProbeOutcome> AttemptAsync(string attempt, Func<Task> action)
    {
        try
        {
            await action();
            return new ProbeOutcome(true, attempt, false, null, "La operación NO fue rechazada.", StatusCodes.Status200OK);
        }
        catch (Exception ex) when (ProblemMapping.Map(ex) is { } problem)
        {
            return new ProbeOutcome(true, attempt, true, problem.Code, problem.Detail, problem.Status);
        }
    }

    /// <summary>Same, for the module API of HallSchedule, which answers with a result instead of throwing.</summary>
    public async Task<ProbeOutcome> AttemptScheduleAsync(string attempt, Func<Task<ScheduleResult>> action)
    {
        var result = await action();
        return result.Success
            ? new ProbeOutcome(true, attempt, false, null, "La operación NO fue rechazada.", StatusCodes.Status200OK)
            : new ProbeOutcome(true, attempt, true, result.Code, result.Reason ?? string.Empty, StatusCodes.Status422UnprocessableEntity);
    }

    public static ProbeOutcome ByDesign(string attempt, string explanation) =>
        new(false, attempt, false, null, explanation, StatusCodes.Status200OK);

    // ---- Fixtures built with the real use cases ----------------------------------------------------------------------

    public Task<IssuedQuoteDto> IssueAsync(
        Guid customer, DateOnly date, int startHour = 10, int endHour = 14, int attendance = 40,
        IReadOnlyCollection<ExtraServiceRequest>? extras = null) =>
        Mediator.Send(new IssueQuoteCommand(
            Actor, customer, HallId, date, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), attendance, extras ?? []));

    public Task<AcceptedQuoteDto> AcceptAsync(Guid quoteId) => Mediator.Send(new AcceptQuoteCommand(quoteId, Actor));

    /// <summary>Issue, accept and let the request be created: the same path a customer's acceptance takes.</summary>
    public async Task<Guid> RequestedEventAsync(Guid customer, DateOnly date, int startHour = 10, int endHour = 14)
    {
        var quote = await IssueAsync(customer, date, startHour, endHour);
        await AcceptAsync(quote.QuoteId);
        await DeliverOutboxAsync();

        var events = await Mediator.Send(new ListHallEventsQuery(CustomerId: customer, From: date, To: date));
        return events.Single(e => e.Start == new TimeOnly(startHour, 0)).Id;
    }

    public Task ApproveAsync(Guid eventId, int percentage = 30) =>
        Mediator.Send(new ApproveEventRequestCommand(eventId, percentage, "Lucía Paz"));

    public Task<AccreditDepositResultDto> PayAsync(Guid eventId, decimal amount, string transactionId, string currency = "BOB") =>
        Mediator.Send(new AccreditDepositCommand(eventId, amount, currency, transactionId));

    public Task CancelAsync(Guid eventId) => Mediator.Send(new CancelHallEventCommand(eventId, "Sonda", Actor));

    /// <summary>
    /// Delivers, inside this transaction, the integration events the use cases just queued. In production the outbox does it
    /// a second later through the bus; here the module handlers run in this scope so they share the rolled-back transaction.
    /// The external systems are skipped: they live in memory and a probe must not leave anything there.
    /// </summary>
    public async Task DeliverOutboxAsync()
    {
        var serializer = Service<IntegrationEventSerializer>();
        var external = typeof(Nodo.External.Simulated.AssemblyMarker).Assembly;

        bool deliveredSomething;
        do
        {
            deliveredSomething = false;
            foreach (var context in new DbContext[] { Service<QuotesDbContext>(), Service<EventLifecycleDbContext>(), Service<HallScheduleDbContext>() })
            {
                var pending = await ((IMessagingDbContext)context).OutboxMessages
                    .Where(m => m.ProcessedOnUtc == null)
                    .OrderBy(m => m.OccurredOnUtc)
                    .ToListAsync();

                foreach (var message in pending.Where(m => _delivered.Add(m.Id)))
                {
                    deliveredSomething = true;
                    var integrationEvent = serializer.Deserialize(message.Type, message.Content);
                    var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
                    foreach (var handler in services.GetServices(handlerType).Where(h => h!.GetType().Assembly != external))
                    {
                        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
                        await (Task)method.Invoke(handler, [integrationEvent, CancellationToken.None])!;
                    }
                }
            }
        }
        while (deliveredSomething);
    }
}
