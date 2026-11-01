using Microsoft.EntityFrameworkCore;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Infrastructure.Persistence;

public sealed class EventLifecycleDbContext(DbContextOptions<EventLifecycleDbContext> options)
    : DbContext(options), IMessagingDbContext
{
    public const string Schema = "event_lifecycle";

    public DbSet<HallEvent> HallEvents => Set<HallEvent>();

    public DbSet<CustomerCredit> CustomerCredits => Set<CustomerCredit>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventLifecycleDbContext).Assembly);
        modelBuilder.ConfigureMessaging();
    }
}
