using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Application;
using Nodo.Quotes.Domain;
using Nodo.TestDoubles;

namespace Nodo.Quotes.UnitTests.UseCases;

internal sealed class FakeQuoteRepository(AggregateTracker tracker) : IQuoteRepository
{
    private readonly Dictionary<QuoteId, Quote> _store = [];

    public IReadOnlyCollection<Quote> All => _store.Values;

    public Task<Quote?> GetByIdAsync(QuoteId id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var quote);
        if (quote is not null)
            tracker.Track(quote);
        return Task.FromResult(quote);
    }

    public Task AddAsync(Quote quote, CancellationToken cancellationToken = default)
    {
        _store[quote.Id] = quote;
        tracker.Track(quote);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Quote>> ListDueForExpirationAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var due = _store.Values.Where(q => q.Status == QuoteStatus.Issued && q.IsExpiredAt(nowUtc)).ToList();
        due.ForEach(tracker.Track);
        return Task.FromResult<IReadOnlyCollection<Quote>>(due);
    }
}

internal sealed class FakeSpacesCatalog : ISpacesCatalog
{
    public static readonly Guid HallId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Task<HallInfo?> GetHallAsync(Guid hallId, CancellationToken cancellationToken = default) =>
        Task.FromResult(hallId == HallId ? new HallInfo(HallId, "Salón Polivalente", 80) : null);

    public Task<IReadOnlyCollection<EquipmentItem>> GetEquipmentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<EquipmentItem>>(
        [
            new("PROJECTOR", "Proyector", 1, 150m), new("TV", "Pantalla TV 55\"", 2, 100m),
            new("SOUND", "Sistema de sonido", 1, 250m), new("LIGHTS", "Kit de luces", 1, 200m)
        ]);

    public Task<IReadOnlyCollection<CateringItem>> GetCateringMenuAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<CateringItem>>(
        [
            new("COFFEE_BREAK", "Coffee break", 35m), new("SNACK", "Refrigerio", 25m),
            new("LUNCH", "Almuerzo ejecutivo", 90m), new("BUFFET", "Cena buffet", 120m)
        ]);
}

internal sealed class FakeMembershipDirectory : IMembershipDirectory
{
    public static readonly Guid Ana = Guid.Parse("22222222-0000-0000-0000-000000000001");        // Flexible, active, 5 %
    public static readonly Guid TechSol = Guid.Parse("22222222-0000-0000-0000-000000000002");    // Equipo, active, 10 %
    public static readonly Guid Carlos = Guid.Parse("22222222-0000-0000-0000-000000000003");    // Dedicado, expired, 8 %
    public static readonly Guid Fundacion = Guid.Parse("22222222-0000-0000-0000-000000000004");  // external

    private static readonly Dictionary<Guid, Membership> Memberships = new()
    {
        [Ana] = new(true, 5), [TechSol] = new(true, 10), [Carlos] = new(false, 8), [Fundacion] = new(false, 0)
    };

    public Task<Membership> GetMembershipAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Memberships.GetValueOrDefault(customerId, new Membership(false, 0)));
}

internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    public Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(customerId == FakeMembershipDirectory.Ana ? "Ana Gutiérrez" : null);
}

internal sealed class QuotesOutbox : InMemoryOutbox, IQuotesOutbox;

internal sealed class QuotesUnitOfWork(IDomainEventDispatcher dispatcher, AggregateTracker tracker)
    : TrackingUnitOfWork(dispatcher, tracker), IQuotesUnitOfWork;

/// <summary>Quotes application layer wired with in-memory fakes and the real MediatR pipeline.</summary>
internal sealed class QtTestHost : IDisposable
{
    public static readonly Guid Hall = FakeSpacesCatalog.HallId;
    public static readonly DateOnly EventDay = new(2026, 11, 11); // 10 days after the default clock (1 Nov)

    private readonly ServiceProvider _provider;

    public QtTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = TestContainer.CreateServices(typeof(Nodo.Quotes.Application.AssemblyMarker).Assembly);
        services.AddQuotesApplication();
        services.AddSingleton<FakeQuoteRepository>();
        services.AddSingleton<IQuoteRepository>(sp => sp.GetRequiredService<FakeQuoteRepository>());
        services.AddSingleton<QuotesOutbox>();
        services.AddSingleton<IQuotesOutbox>(sp => sp.GetRequiredService<QuotesOutbox>());
        services.AddSingleton<QuotesUnitOfWork>();
        services.AddSingleton<IQuotesUnitOfWork>(sp => sp.GetRequiredService<QuotesUnitOfWork>());
        services.AddSingleton<ISpacesCatalog, FakeSpacesCatalog>();
        services.AddSingleton<IMembershipDirectory, FakeMembershipDirectory>();
        services.AddSingleton<ICustomerDirectory, FakeCustomerDirectory>();
        services.AddSingleton(Substitute.For<IQuoteQueries>());

        var hallSchedule = Substitute.For<IHallScheduleModuleApi>();
        hallSchedule.CheckAvailabilityAsync(default, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(ScheduleResult.Ok());
        services.AddSingleton(hallSchedule);

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public IServiceProvider Provider => _provider;

    public IMediator Mediator => Scoped<IMediator>();
    // Each use case runs in its own DI scope, like one HTTP request or one message: scoped services never leak between calls.
    private T Scoped<T>() where T : notnull => _provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    public FakeQuoteRepository Quotes => _provider.GetRequiredService<FakeQuoteRepository>();

    public QuotesOutbox Outbox => _provider.GetRequiredService<QuotesOutbox>();

    public QuotesUnitOfWork UnitOfWork => _provider.GetRequiredService<QuotesUnitOfWork>();

    public FakeClock Clock => _provider.GetRequiredService<FakeClock>();

    public IHallScheduleModuleApi HallSchedule => _provider.GetRequiredService<IHallScheduleModuleApi>();

    public IQuoteQueries Queries => _provider.GetRequiredService<IQuoteQueries>();

    public void HallScheduleIsBusy(string code = "SCHEDULE.SLOT_OVERLAP", string reason = "El salón está ocupado.") =>
        HallSchedule.CheckAvailabilityAsync(default, default, default, default, default!, default, default)
            .ReturnsForAnyArgs(ScheduleResult.Fail(code, reason));

    public IssueQuoteCommand IssueCommand(
        Guid? customerId = null, int startHour = 9, int endHour = 13, int attendance = 40,
        IReadOnlyCollection<ExtraServiceRequest>? extras = null, DateOnly? date = null) =>
        new("Recepción Nodo", customerId ?? FakeMembershipDirectory.Ana, Hall, date ?? EventDay,
            new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), attendance, extras ?? []);

    public Task<IssuedQuoteDto> IssueAsync(IssueQuoteCommand? command = null) =>
        Mediator.Send(command ?? IssueCommand());

    public void Dispose() => _provider.Dispose();
}
