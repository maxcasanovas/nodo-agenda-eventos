using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Application;

// The query ports return the data; handlers add the customer name (an external fact) and guarantee ordering.

// ---- GetHallEvent ----

public sealed record GetHallEventQuery(Guid EventId) : IQuery<HallEventDto>;

internal sealed class GetHallEventValidator : AbstractValidator<GetHallEventQuery>
{
    public GetHallEventValidator() => RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
}

internal sealed class GetHallEventHandler(IHallEventQueries queries, ICustomerDirectory customers)
    : IQueryHandler<GetHallEventQuery, HallEventDto>
{
    public async Task<HallEventDto> Handle(GetHallEventQuery request, CancellationToken cancellationToken)
    {
        var hallEvent = await queries.GetAsync(request.EventId, cancellationToken)
            ?? throw new NotFoundException($"No existe el evento '{request.EventId}'.");

        return hallEvent with { CustomerName = await customers.GetNameAsync(hallEvent.CustomerId, cancellationToken) };
    }
}

// ---- ListHallEvents ----

public sealed record ListHallEventsQuery(
    string? Status = null, Guid? CustomerId = null, DateOnly? From = null, DateOnly? To = null)
    : IQuery<IReadOnlyCollection<HallEventListItemDto>>;

internal sealed class ListHallEventsValidator : AbstractValidator<ListHallEventsQuery>
{
    public ListHallEventsValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<HallEventStatus>(s, ignoreCase: true, out _))
            .WithMessage($"El estado debe ser uno de: {string.Join(", ", Enum.GetNames<HallEventStatus>())}.");
        RuleFor(x => x.To)
            .Must((q, to) => to is null || q.From is null || to >= q.From)
            .WithMessage("El rango de fechas es inválido.");
    }
}

internal sealed class ListHallEventsHandler(IHallEventQueries queries, ICustomerDirectory customers)
    : IQueryHandler<ListHallEventsQuery, IReadOnlyCollection<HallEventListItemDto>>
{
    public async Task<IReadOnlyCollection<HallEventListItemDto>> Handle(
        ListHallEventsQuery request, CancellationToken cancellationToken)
    {
        var items = await queries.ListAsync(request.Status, request.CustomerId, request.From, request.To, cancellationToken);
        return await ListEnricher.WithNamesAsync(items, customers, cancellationToken);
    }
}

// ---- GetPendingApprovals (Management's inbox, most urgent first) ----

public sealed record GetPendingApprovalsQuery : IQuery<IReadOnlyCollection<HallEventListItemDto>>;

internal sealed class GetPendingApprovalsHandler(IHallEventQueries queries, ICustomerDirectory customers)
    : IQueryHandler<GetPendingApprovalsQuery, IReadOnlyCollection<HallEventListItemDto>>
{
    public async Task<IReadOnlyCollection<HallEventListItemDto>> Handle(
        GetPendingApprovalsQuery request, CancellationToken cancellationToken)
    {
        var items = await queries.GetPendingApprovalsAsync(cancellationToken);
        var ordered = items.OrderBy(i => i.DecisionDeadlineUtc).ToList();
        return await ListEnricher.WithNamesAsync(ordered, customers, cancellationToken);
    }
}

// ---- GetTodayOperations (Operations' view of the local day) ----

public sealed record GetTodayOperationsQuery : IQuery<IReadOnlyCollection<HallEventListItemDto>>;

internal sealed class GetTodayOperationsHandler(IHallEventQueries queries, ICustomerDirectory customers, IClock clock)
    : IQueryHandler<GetTodayOperationsQuery, IReadOnlyCollection<HallEventListItemDto>>
{
    public async Task<IReadOnlyCollection<HallEventListItemDto>> Handle(
        GetTodayOperationsQuery request, CancellationToken cancellationToken)
    {
        var items = await queries.GetTodayOperationsAsync(clock.TodayLocal, cancellationToken);
        var ordered = items.OrderBy(i => i.Start).ToList();
        return await ListEnricher.WithNamesAsync(ordered, customers, cancellationToken);
    }
}

// ---- GetCustomerCredit ----

public sealed record GetCustomerCreditQuery(Guid CustomerId) : IQuery<CustomerCreditDto>;

internal sealed class GetCustomerCreditValidator : AbstractValidator<GetCustomerCreditQuery>
{
    public GetCustomerCreditValidator() => RuleFor(x => x.CustomerId).NotEmpty().WithMessage("El cliente es obligatorio.");
}

internal sealed class GetCustomerCreditHandler(IHallEventQueries queries, ICustomerDirectory customers)
    : IQueryHandler<GetCustomerCreditQuery, CustomerCreditDto>
{
    public async Task<CustomerCreditDto> Handle(GetCustomerCreditQuery request, CancellationToken cancellationToken)
    {
        var name = await customers.GetNameAsync(request.CustomerId, cancellationToken);
        var credit = await queries.GetCreditAsync(request.CustomerId, cancellationToken);

        // A customer who never had credit simply has a zero balance.
        return credit is null
            ? new CustomerCreditDto(request.CustomerId, name, 0m, [])
            : credit with { CustomerName = name };
    }
}

// ---- Module API for other modules (the hall calendar) ----

internal sealed class EventLifecycleModuleApi(IHallEventQueries queries, ICustomerDirectory customers)
    : IEventLifecycleModuleApi
{
    public async Task<IReadOnlyCollection<EventSummary>> GetEventSummariesAsync(
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        var summaries = await queries.GetSummariesAsync(eventIds, cancellationToken);

        var result = new List<EventSummary>(summaries.Count);
        foreach (var summary in summaries)
        {
            result.Add(summary with { CustomerName = await customers.GetNameAsync(summary.CustomerId, cancellationToken) });
        }

        return result;
    }
}

internal static class ListEnricher
{
    public static async Task<IReadOnlyCollection<HallEventListItemDto>> WithNamesAsync(
        IReadOnlyCollection<HallEventListItemDto> items, ICustomerDirectory customers, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var customerId in items.Select(i => i.CustomerId).Distinct())
            names[customerId] = await customers.GetNameAsync(customerId, cancellationToken);

        return items.Select(i => i with { CustomerName = names[i.CustomerId] }).ToList();
    }
}
