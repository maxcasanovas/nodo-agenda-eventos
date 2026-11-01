using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;
using Nodo.TestDoubles;
using Schedule = Nodo.HallSchedule.Domain.HallSchedule;

namespace Nodo.HallSchedule.UnitTests.UseCases;

internal sealed class FakeHallScheduleRepository(AggregateTracker tracker) : IHallScheduleRepository
{
    private readonly Dictionary<HallScheduleId, Schedule> _store = [];

    public int SaveCalls { get; private set; }

    public Task<Schedule> GetOrCreateAsync(HallId hallId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var id = new HallScheduleId(hallId, date);
        if (!_store.TryGetValue(id, out var schedule))
            schedule = Schedule.Create(hallId, date);

        tracker.Track(schedule);
        return Task.FromResult(schedule);
    }

    public Task SaveAsync(Schedule schedule, CancellationToken cancellationToken = default)
    {
        _store[schedule.Id] = schedule;
        SaveCalls++;
        return Task.CompletedTask;
    }

    public bool Contains(HallId hallId, DateOnly date) => _store.ContainsKey(new HallScheduleId(hallId, date));

    public Schedule? Peek(HallId hallId, DateOnly date) => _store.GetValueOrDefault(new HallScheduleId(hallId, date));
}

internal sealed class FakeSpacesCatalog : ISpacesCatalog
{
    public IReadOnlyDictionary<string, int> Stock { get; set; } = new Dictionary<string, int>
    {
        ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1
    };

    public Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stock);
}

internal sealed class HsOutbox : InMemoryOutbox, IHallScheduleOutbox;

internal sealed class HsUnitOfWork(IDomainEventDispatcher dispatcher, AggregateTracker tracker)
    : TrackingUnitOfWork(dispatcher, tracker), IHallScheduleUnitOfWork;

/// <summary>HallSchedule application layer wired with in-memory fakes and the real MediatR pipeline.</summary>
internal sealed class HsTestHost : IDisposable
{
    public static readonly Guid HallGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly DateOnly Day = new(2026, 11, 10);

    private readonly ServiceProvider _provider;

    public HsTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = TestContainer.CreateServices(typeof(Nodo.HallSchedule.Application.AssemblyMarker).Assembly);
        services.AddHallScheduleApplication();
        services.AddSingleton<FakeHallScheduleRepository>();
        services.AddSingleton<IHallScheduleRepository>(sp => sp.GetRequiredService<FakeHallScheduleRepository>());
        services.AddSingleton<HsOutbox>();
        services.AddSingleton<IHallScheduleOutbox>(sp => sp.GetRequiredService<HsOutbox>());
        services.AddSingleton<HsUnitOfWork>();
        services.AddSingleton<IHallScheduleUnitOfWork>(sp => sp.GetRequiredService<HsUnitOfWork>());
        services.AddSingleton<FakeSpacesCatalog>();
        services.AddSingleton<ISpacesCatalog>(sp => sp.GetRequiredService<FakeSpacesCatalog>());
        services.AddSingleton(Substitute.For<IHallCalendarQueries>());
        services.AddSingleton(Substitute.For<IEventLifecycleModuleApi>());
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Provider => _provider;

    // Each use case runs in its own DI scope, like one HTTP request or one message: scoped services never leak between calls.
    private T Scoped<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    public IMediator Mediator => Scoped<IMediator>();

    public IHallScheduleModuleApi Api => Scoped<IHallScheduleModuleApi>();

    public FakeHallScheduleRepository Schedules => _provider.GetRequiredService<FakeHallScheduleRepository>();

    public HsOutbox Outbox => _provider.GetRequiredService<HsOutbox>();

    public HsUnitOfWork UnitOfWork => _provider.GetRequiredService<HsUnitOfWork>();

    public FakeSpacesCatalog Spaces => _provider.GetRequiredService<FakeSpacesCatalog>();

    public FakeClock Clock => _provider.GetRequiredService<FakeClock>();

    public IHallCalendarQueries CalendarQueries => _provider.GetRequiredService<IHallCalendarQueries>();

    public IEventLifecycleModuleApi EventLifecycle => _provider.GetRequiredService<IEventLifecycleModuleApi>();

    public IIntegrationEventHandler<T> IntegrationHandler<T>() where T : IIntegrationEvent =>
        Scoped<IIntegrationEventHandler<T>>();

    public async Task<ScheduleResult> PlaceProvisionalAsync(Guid eventId, int startHour, int endHour, params EquipmentRequest[] equipment) =>
        await Api.PlaceProvisionalBlockAsync(
            eventId, HallGuid, Day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), equipment);

    public void Dispose() => _provider.Dispose();
}
