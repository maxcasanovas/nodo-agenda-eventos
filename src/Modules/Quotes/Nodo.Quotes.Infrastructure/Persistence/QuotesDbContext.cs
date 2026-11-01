using Microsoft.EntityFrameworkCore;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Infrastructure.Persistence;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options), IMessagingDbContext
{
    public const string Schema = "quotes";

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QuotesDbContext).Assembly);
        modelBuilder.ConfigureMessaging();
    }
}
