using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Domain;
using Nodo.HallSchedule.Infrastructure.Persistence;

namespace Nodo.HallSchedule.Infrastructure;

internal static class ConstraintCodes
{
    public static readonly InvariantConstraintMap Map = InvariantConstraintMap.Of(
        ("INV-01", Domain.InvariantCodes.DurationOutOfRange),
        ("INV-18", Domain.InvariantCodes.SlotOverlap),
        ("INV-19", Domain.InvariantCodes.DuplicateBlock));
}

internal sealed class HallScheduleUnitOfWork(HallScheduleDbContext context, IDomainEventDispatcher dispatcher)
    : EfUnitOfWork<HallScheduleDbContext>(context, dispatcher, ConstraintCodes.Map), IHallScheduleUnitOfWork;

internal sealed class HallScheduleOutbox(HallScheduleDbContext context, IntegrationEventSerializer serializer)
    : EfOutbox<HallScheduleDbContext>(context, serializer), IHallScheduleOutbox;

internal sealed class HallScheduleInbox(HallScheduleDbContext context, IClock clock)
    : EfInbox<HallScheduleDbContext>(context, clock), IHallScheduleInbox;

public static class DependencyInjection
{
    public static IServiceCollection AddHallScheduleInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HallScheduleDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__ef_migrations_history", HallScheduleDbContext.Schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IHallScheduleRepository, HallScheduleRepository>();
        services.AddScoped<IHallCalendarQueries, HallCalendarQueries>();
        services.AddScoped<IHallScheduleUnitOfWork, HallScheduleUnitOfWork>();
        services.AddScoped<IHallScheduleOutbox, HallScheduleOutbox>();
        services.AddScoped<IHallScheduleInbox, HallScheduleInbox>();
        services.AddScoped<ISpacesCatalog, SpacesCatalogAdapter>(); // adapter towards Spaces and Resources

        services.AddSingleton(new ModuleMessaging(
            typeof(Application.AssemblyMarker).Assembly,
            sp => sp.GetRequiredService<IHallScheduleInbox>(),
            sp => sp.GetRequiredService<IHallScheduleUnitOfWork>()));
        services.AddHostedService<OutboxProcessor<HallScheduleDbContext>>();
        return services;
    }
}
