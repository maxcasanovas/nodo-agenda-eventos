using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Jobs;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Domain;
using Nodo.EventLifecycle.Infrastructure.Persistence;

namespace Nodo.EventLifecycle.Infrastructure;

internal static class ConstraintCodes
{
    public static readonly InvariantConstraintMap Map = InvariantConstraintMap.Of(
        ("INV-01", Domain.InvariantCodes.DurationOutOfRange),
        ("INV-02", Domain.InvariantCodes.DepositPercentageOutOfRange),
        ("INV-04", Domain.InvariantCodes.AttendanceOutOfRange),
        ("INV-05", Domain.InvariantCodes.CancellationNotAllowed),
        ("INV-21", Domain.InvariantCodes.CreditInsufficient));
}

internal sealed class EventLifecycleUnitOfWork(EventLifecycleDbContext context, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<EventLifecycleDbContext>(context, dispatcher, ConstraintCodes.Map), IEventLifecycleUnitOfWork;

internal sealed class EventLifecycleOutbox(EventLifecycleDbContext context, IntegrationEventSerializer serializer)
    : EfOutbox<EventLifecycleDbContext>(context, serializer), IEventLifecycleOutbox;

internal sealed class EventLifecycleInbox(EventLifecycleDbContext context, IClock clock)
    : EfInbox<EventLifecycleDbContext>(context, clock), IEventLifecycleInbox;

/// <summary>Expires requests Management ignored (BR-12) and unpaid deposits (BR-17) every 30 s.</summary>
internal sealed class HallEventExpirationJob(IServiceScopeFactory scopeFactory, ILogger<HallEventExpirationJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    public override string Name => "HallEventExpirationJob";

    protected override async Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        await scopedServices.GetRequiredService<MediatR.IMediator>().Send(new ExpireOverdueHallEventsCommand(), cancellationToken);
}

public static class DependencyInjection
{
    public static IServiceCollection AddEventLifecycleInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<EventLifecycleDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", EventLifecycleDbContext.Schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IHallEventRepository, HallEventRepository>();
        services.AddScoped<ICustomerCreditRepository, CustomerCreditRepository>();
        services.AddScoped<IHallEventQueries, HallEventQueries>();
        services.AddScoped<IEventLifecycleUnitOfWork, EventLifecycleUnitOfWork>();
        services.AddScoped<IEventLifecycleOutbox, EventLifecycleOutbox>();
        services.AddScoped<IEventLifecycleInbox, EventLifecycleInbox>();

        // Adapters towards the external contexts (anti-corruption at the border).
        services.AddScoped<ISpacesCatalog, SpacesCatalogAdapter>();
        services.AddScoped<ICustomerDirectory, CustomerDirectoryAdapter>();
        services.AddScoped<IPaymentGatewayAcl, PaymentGatewayAclAdapter>();

        services.AddSingleton(new ModuleMessaging(
            typeof(Application.AssemblyMarker).Assembly,
            sp => sp.GetRequiredService<IEventLifecycleInbox>(),
            sp => sp.GetRequiredService<IEventLifecycleUnitOfWork>()));
        services.AddHostedService<OutboxProcessor<EventLifecycleDbContext>>();
        services.AddSingleton<HallEventExpirationJob>();
        services.AddSingleton<IRunnableJob>(sp => sp.GetRequiredService<HallEventExpirationJob>());
        services.AddHostedService(sp => sp.GetRequiredService<HallEventExpirationJob>());
        return services;
    }
}
