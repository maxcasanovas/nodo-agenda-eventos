using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Contracts;
using Nodo.SharedKernel;
using Nodo.TestDoubles;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

internal sealed class FakeHallEventRepository(AggregateTracker tracker) : IHallEventRepository
{
    private readonly Dictionary<HallEventId, HallEvent> _store = [];

    public IReadOnlyCollection<HallEvent> All => _store.Values;

    public Task<HallEvent?> GetByIdAsync(HallEventId id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var hallEvent);
        if (hallEvent is not null)
            tracker.Track(hallEvent);
        return Task.FromResult(hallEvent);
    }

    public Task AddAsync(HallEvent hallEvent, CancellationToken cancellationToken = default)
    {
        _store[hallEvent.Id] = hallEvent;
        tracker.Track(hallEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsActiveForSlotAsync(HallId hallId, TimeSlot slot, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyCollection<HallEvent>> ListOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var overdue = _store.Values
            .Where(e => (e.Status == HallEventStatus.Requested && nowUtc >= e.DecisionDeadlineUtc) ||
                        (e.Status == HallEventStatus.ApprovedByManagement && nowUtc >= e.DepositDeadlineUtc))
            .ToList();
        overdue.ForEach(tracker.Track);
        return Task.FromResult<IReadOnlyCollection<HallEvent>>(overdue);
    }
}

internal sealed class FakeCustomerCreditRepository(AggregateTracker tracker) : ICustomerCreditRepository
{
    private readonly Dictionary<CustomerId, CustomerCredit> _store = [];

    public IReadOnlyCollection<CustomerCredit> All => _store.Values;

    public CustomerCredit? Peek(CustomerId customerId) => _store.GetValueOrDefault(customerId);

    public void Remove(CustomerId customerId) => _store.Remove(customerId);

    public Task<CustomerCredit?> GetByCustomerAsync(CustomerId customerId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(customerId, out var credit);
        if (credit is not null)
            tracker.Track(credit);
        return Task.FromResult(credit);
    }

    public Task AddAsync(CustomerCredit credit, CancellationToken cancellationToken = default)
    {
        _store[credit.Id] = credit;
        tracker.Track(credit);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSpacesCatalog : ISpacesCatalog
{
    public Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>
        {
            ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1
        });
}

internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    public Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(customerId == ElTestHost.TechSol ? "TechSol S.R.L." : null);
}

internal sealed class ElOutbox : InMemoryOutbox, IEventLifecycleOutbox;

internal sealed class ElInbox(AggregateTracker tracker) : InMemoryInbox(tracker), IEventLifecycleInbox;

internal sealed class ElUnitOfWork(IDomainEventDispatcher dispatcher, AggregateTracker tracker)
    : TrackingUnitOfWork(dispatcher, tracker), IEventLifecycleUnitOfWork;

/// <summary>EventLifecycle application layer wired with in-memory fakes and the real MediatR pipeline.</summary>
internal sealed class ElTestHost : IDisposable
{
    public static readonly Guid TechSol = Guid.Parse("22222222-0000-0000-0000-000000000002");
    public static readonly Guid Hall = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateOnly EventDay = new(2026, 11, 11); // 10 days after the default clock (1 Nov)
    public const string Management = "Lucía Paz";
    public const string Reception = "Recepción Nodo";
    public const string Operations = "Host de turno";

    private readonly ServiceProvider _provider;

    public ElTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = TestContainer.CreateServices(typeof(Nodo.EventLifecycle.Application.AssemblyMarker).Assembly);
        services.AddEventLifecycleApplication();
        services.AddSingleton<FakeHallEventRepository>();
        services.AddSingleton<IHallEventRepository>(sp => sp.GetRequiredService<FakeHallEventRepository>());
        services.AddSingleton<FakeCustomerCreditRepository>();
        services.AddSingleton<ICustomerCreditRepository>(sp => sp.GetRequiredService<FakeCustomerCreditRepository>());
        services.AddSingleton<ElOutbox>();
        services.AddSingleton<IEventLifecycleOutbox>(sp => sp.GetRequiredService<ElOutbox>());
        services.AddSingleton<ElInbox>();
        services.AddSingleton<IEventLifecycleInbox>(sp => sp.GetRequiredService<ElInbox>());
        services.AddSingleton<ElUnitOfWork>();
        services.AddSingleton<IEventLifecycleUnitOfWork>(sp => sp.GetRequiredService<ElUnitOfWork>());
        services.AddSingleton<ISpacesCatalog, FakeSpacesCatalog>();
        services.AddSingleton<ICustomerDirectory, FakeCustomerDirectory>();
        services.AddSingleton(Substitute.For<IHallEventQueries>());

        var hallSchedule = Substitute.For<IHallScheduleModuleApi>();
        hallSchedule.CheckAvailabilityAsync(default, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(ScheduleResult.Ok());
        hallSchedule.PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default)
            .ReturnsForAnyArgs(ScheduleResult.Ok());
        hallSchedule.PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default)
            .ReturnsForAnyArgs(ScheduleResult.Ok());
        services.AddSingleton(hallSchedule);

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Provider => _provider;

    // Each use case runs in its own DI scope, like one HTTP request or one message: scoped services never leak between calls.
    private T Scoped<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    public IMediator Mediator => Scoped<IMediator>();

    public FakeHallEventRepository Events => _provider.GetRequiredService<FakeHallEventRepository>();

    public FakeCustomerCreditRepository Credits => _provider.GetRequiredService<FakeCustomerCreditRepository>();

    public ElOutbox Outbox => _provider.GetRequiredService<ElOutbox>();

    public ElUnitOfWork UnitOfWork => _provider.GetRequiredService<ElUnitOfWork>();

    public FakeClock Clock => _provider.GetRequiredService<FakeClock>();

    public IHallScheduleModuleApi HallSchedule => _provider.GetRequiredService<IHallScheduleModuleApi>();

    public IHallEventQueries Queries => _provider.GetRequiredService<IHallEventQueries>();

    /// <summary>The policy as the message bus runs it: wrapped by the inbox decorator, which provides idempotency.</summary>
    public IIntegrationEventHandler<QuoteAcceptedIntegrationEvent> QuoteAcceptedHandler
    {
        get
        {
            var inner = Scoped<IIntegrationEventHandler<QuoteAcceptedIntegrationEvent>>();
            return new InboxIntegrationEventHandler<QuoteAcceptedIntegrationEvent>(
                inner, _provider.GetRequiredService<IEventLifecycleInbox>(),
                _provider.GetRequiredService<IEventLifecycleUnitOfWork>(), inner.GetType().Name);
        }
    }

    public void Dispose() => _provider.Dispose();

    // ---- Failure switches for the hall schedule ----

    public void HallScheduleIsBusy(string code = "SCHEDULE.SLOT_OVERLAP", string reason = "El salón está ocupado.")
    {
        var busy = ScheduleResult.Fail(code, reason);
        HallSchedule.CheckAvailabilityAsync(default, default, default, default, default!, default, default).ReturnsForAnyArgs(busy);
        HallSchedule.PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(busy);
        HallSchedule.PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(busy);
    }

    /// <summary>Availability checks pass but the reservation itself loses a race.</summary>
    public void HallScheduleRejectsReservations(string code = "SCHEDULE.SLOT_OVERLAP", string reason = "Otro evento la tomó.")
    {
        var busy = ScheduleResult.Fail(code, reason);
        HallSchedule.PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(busy);
        HallSchedule.PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default).ReturnsForAnyArgs(busy);
    }

    // ---- Scenario builders (through the public use cases only) ----

    public static QuoteAcceptedIntegrationEvent QuoteAcceptedMessage(
        Guid? messageId = null, DateOnly? date = null, int startHour = 9, int endHour = 13, int attendance = 40,
        decimal total = 2000m, params QuoteExtraServiceDto[] extras) => new(
        messageId ?? Guid.NewGuid(), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), TechSol, Hall,
        date ?? EventDay, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), attendance, 500m, endHour - startHour,
        0.9m, total, 10, 0m, extras.Sum(e => e.Quantity * e.UnitCost), total, extras);

    public static QuoteExtraServiceDto Projector() => new("OwnEquipment", "PROJECTOR", "Proyector", 1, 150m);

    public static QuoteExtraServiceDto CoffeeBreak(int people = 40) =>
        new("ExternalCatering", "COFFEE_BREAK", "Coffee break", people, 35m);

    public async Task<HallEvent> RequestedEventAsync(QuoteAcceptedIntegrationEvent? message = null)
    {
        var before = Events.All.ToHashSet();
        await QuoteAcceptedHandler.HandleAsync(message ?? QuoteAcceptedMessage());
        return Events.All.Single(e => !before.Contains(e));
    }

    public async Task<HallEvent> ApprovedEventAsync(int percentage = 30, QuoteAcceptedIntegrationEvent? message = null)
    {
        var hallEvent = await RequestedEventAsync(message);
        await Mediator.Send(new ApproveEventRequestCommand(hallEvent.Id.Value, percentage, Management));
        return hallEvent;
    }

    /// <summary>Approved at 30 % (600 Bs of a 2.000 total) and fully paid.</summary>
    public async Task<HallEvent> ConfirmedEventAsync(QuoteAcceptedIntegrationEvent? message = null)
    {
        var hallEvent = await ApprovedEventAsync(30, message);
        await Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", $"TX-{Guid.NewGuid():N}"));
        return hallEvent;
    }

    /// <summary>Confirmed, then cancelled: 600 Bs of credit for TechSol.</summary>
    public async Task<HallEvent> CancelledEventWithCreditAsync(QuoteAcceptedIntegrationEvent? message = null)
    {
        var hallEvent = await ConfirmedEventAsync(message);
        await Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "Cambio de planes", Reception));
        return hallEvent;
    }

    public void ResetObservations()
    {
        Outbox.Clear();
        HallSchedule.ClearReceivedCalls();
    }
}
