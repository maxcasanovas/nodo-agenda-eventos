using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Infrastructure.Persistence.Configurations;

internal sealed class HallEventConfiguration : IEntityTypeConfiguration<HallEvent>
{
    public void Configure(EntityTypeBuilder<HallEvent> builder)
    {
        // Database defense in depth for the invariants of the catalog (business-rules.md §9). The domain rejects the
        // same input first with a 422; these constraints also cover direct writes and concurrent requests.
        builder.ToTable("hall_events", t =>
        {
            t.HasCheckConstraint("ck_hall_events_inv01_duration",
                "end_time > start_time AND end_time - start_time BETWEEN interval '1 hour' AND interval '8 hours'");
            t.HasCheckConstraint("ck_hall_events_inv02_deposit_percentage",
                "deposit_percentage IS NULL OR deposit_percentage BETWEEN 20 AND 50");
            t.HasCheckConstraint("ck_hall_events_inv04_attendance", "attendance BETWEEN 1 AND 80");
            t.HasCheckConstraint("ck_hall_events_inv05_credit_generated", "credit_generated_amount >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, value => new HallEventId(value)).ValueGeneratedNever();
        builder.Property(x => x.QuoteId).HasConversion(id => id.Value, value => new QuoteId(value));
        builder.Property(x => x.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value));
        builder.Property(x => x.HallId).HasConversion(id => id.Value, value => new HallId(value));

        // Single-column value object: loading goes through Create, so a corrupt row can never become an invalid Attendance.
        builder.Property(x => x.Attendance)
            .HasConversion(a => a.Value, value => Attendance.Create(value))
            .HasColumnName("attendance");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RequestedAtUtc);
        builder.Property(x => x.DecisionDeadlineUtc);
        builder.Property(x => x.DepositDeadlineUtc);
        builder.Property(x => x.RescheduleCount);
        builder.Property(x => x.ExtraServicesAssigned);
        builder.Property<uint>("xmin").IsRowVersion();

        // Value objects are complex types: no identity, so instances can be shared between properties and replaced freely.
        builder.ComplexProperty(x => x.Slot, slot =>
        {
            slot.Property(s => s.Date).HasColumnName("date");
            slot.Property(s => s.Start).HasColumnName("start_time");
            slot.Property(s => s.End).HasColumnName("end_time");
            slot.Ignore(s => s.DurationHours);
            slot.Ignore(s => s.StartUtc);
            slot.Ignore(s => s.EndUtc);
        });

        builder.ComplexProperty(x => x.Total, m => m.ConfigureMoney("total"));
        builder.ComplexProperty(x => x.CreditGenerated, m => m.ConfigureMoney("credit_generated"));
        builder.ComplexProperty(x => x.RemainingToSettle, m => m.ConfigureMoney("remaining_to_settle"));

        // Optional until Management approves. The paid transaction ids live in a text[] column of the value object
        // (they are part of its immutable value); the domain keeps each id from being accredited twice.
        builder.ComplexProperty(x => x.Deposit, deposit =>
        {
            deposit.Property(d => d.Percentage).HasColumnName("deposit_percentage");
            deposit.ComplexProperty(d => d.Required, m => m.ConfigureMoney("deposit_required"));
            deposit.ComplexProperty(d => d.Accredited, m => m.ConfigureMoney("deposit_accredited"));
            deposit.Property<string[]>("_transactionIds").HasColumnName("deposit_transaction_ids");
            deposit.Ignore(d => d.TransactionIds);
            deposit.Ignore(d => d.IsCovered);
            deposit.Ignore(d => d.Pending);
        });

        builder.ComplexProperty(x => x.Decision, decision =>
        {
            decision.Property(d => d.DecidedBy).HasColumnName("decided_by").HasMaxLength(100);
            decision.Property(d => d.DecidedAtUtc).HasColumnName("decided_at_utc");
            decision.Property(d => d.Approved).HasColumnName("decision_approved");
            decision.Property(d => d.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        });

        builder.HasMany(x => x.ExtraServices)
            .WithOne()
            .HasForeignKey("HallEventId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.ExtraServices).HasField("_extraServices").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(x => x.History, history =>
        {
            history.ToTable("hall_event_history");
            history.WithOwner().HasForeignKey("HallEventId");
            history.Property<int>("Id");
            history.HasKey("Id");
            history.Property(h => h.Status).HasConversion<string>().HasMaxLength(30);
            history.Property(h => h.OccurredAtUtc);
            history.Property(h => h.Actor).HasMaxLength(100);
            history.Property(h => h.Note).HasMaxLength(1000);
        });
        builder.Navigation(x => x.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => new { x.Status, x.DecisionDeadlineUtc });
        builder.HasIndex(x => new { x.HallId, x.Status });
    }
}

internal sealed class ExtraServiceItemConfiguration : IEntityTypeConfiguration<ExtraServiceItem>
{
    public void Configure(EntityTypeBuilder<ExtraServiceItem> builder)
    {
        builder.ToTable("extra_service_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, value => new ExtraServiceItemId(value)).ValueGeneratedNever();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Description).HasMaxLength(100);
        builder.Property(x => x.Quantity);
        builder.ComplexProperty(x => x.UnitCost, m => m.ConfigureMoney("unit_cost"));
        builder.Ignore(x => x.LineTotal);
    }
}
