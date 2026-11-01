using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

/// <summary>A published integration event waiting to be delivered (Transactional Outbox).</summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 5;

    public Guid Id { get; set; }

    /// <summary>Name of the contract type, e.g. QuoteAcceptedIntegrationEvent.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The event serialized as JSON (jsonb).</summary>
    public string Content { get; set; } = string.Empty;

    public DateTime OccurredOnUtc { get; set; }

    public DateTime? ProcessedOnUtc { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}

/// <summary>Record of a message already handled by a consumer, so redelivery is harmless.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }

    public string Handler { get; set; } = string.Empty;

    public DateTime ProcessedOnUtc { get; set; }
}

/// <summary>Implemented by each module's DbContext: it owns its outbox and inbox tables inside its own schema.</summary>
public interface IMessagingDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<InboxMessage> InboxMessages { get; }
}

public static class MessagingModelBuilderExtensions
{
    /// <summary>Maps <c>outbox_messages</c> and <c>inbox_messages</c> (the default schema is already the module's).</summary>
    public static ModelBuilder ConfigureMessaging(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(200).IsRequired();
            b.Property(x => x.Content).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.Error).HasMaxLength(2000);
            b.HasIndex(x => x.OccurredOnUtc).HasFilter("processed_on_utc IS NULL").HasDatabaseName("ix_outbox_messages_pending");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(x => new { x.MessageId, x.Handler });
            b.Property(x => x.Handler).HasMaxLength(200).IsRequired();
        });

        return modelBuilder;
    }
}
