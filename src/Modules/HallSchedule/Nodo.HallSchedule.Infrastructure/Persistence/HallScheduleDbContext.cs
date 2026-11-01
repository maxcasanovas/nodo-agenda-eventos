using Microsoft.EntityFrameworkCore;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Infrastructure.Persistence;

public sealed class HallScheduleDbContext(DbContextOptions<HallScheduleDbContext> options)
    : DbContext(options), IMessagingDbContext
{
    public const string Schema = "hall_schedule";

    public DbSet<HallScheduleAggregate> Schedules => Set<HallScheduleAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HallScheduleDbContext).Assembly);
        modelBuilder.ConfigureMessaging();
    }
}
