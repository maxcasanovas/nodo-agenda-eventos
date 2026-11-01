using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Infrastructure.Persistence.Configurations;

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        // Database defense in depth for the invariants of the catalog (business-rules.md §9). The domain rejects the
        // same input first with a 422; these constraints also cover direct writes and concurrent requests.
        builder.ToTable("quotes", t =>
        {
            t.HasCheckConstraint("ck_quotes_inv01_duration",
                "end_time > start_time AND end_time - start_time BETWEEN interval '1 hour' AND interval '8 hours'");
            t.HasCheckConstraint("ck_quotes_inv04_attendance", "attendance BETWEEN 1 AND 80");
            t.HasCheckConstraint("ck_quotes_inv14_validity_96h", "expires_at_utc = issued_at_utc + interval '96 hours'");
            t.HasCheckConstraint("ck_quotes_inv17_membership_discount", "membership_discount_percentage BETWEEN 0 AND 10");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, value => new QuoteId(value)).ValueGeneratedNever();
        builder.Property(x => x.CustomerId).HasConversion(id => id.Value, value => new CustomerId(value));
        builder.Property(x => x.HallId).HasConversion(id => id.Value, value => new HallId(value));

        // Single-column value object: loading goes through Create, so a corrupt row can never become an invalid Attendance.
        builder.Property(x => x.Attendance)
            .HasConversion(a => a.Value, value => Attendance.Create(value))
            .HasColumnName("attendance");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.IssuedAtUtc);
        builder.Property(x => x.ExpiresAtUtc);
        builder.Property(x => x.AcceptedAtUtc);
        builder.Property<uint>("xmin").IsRowVersion();

        // Value objects are complex types: no identity, so instances can be shared between properties and replaced freely.
        builder.ComplexProperty(x => x.Slot, slot =>
        {
            slot.Property(s => s.Date).HasColumnName("date");
            slot.Property(s => s.Start).HasColumnName("start_time");
            slot.Property(s => s.End).HasColumnName("end_time");
            slot.Ignore(s => s.DurationHours);
        });

        builder.ComplexProperty(x => x.Price, price =>
        {
            price.Property(p => p.Hours).HasColumnName("hours");
            price.Property(p => p.RateFactor).HasColumnName("rate_factor").HasPrecision(4, 2);
            price.Property(p => p.MembershipDiscountPercentage).HasColumnName("membership_discount_percentage");
            price.ComplexProperty(p => p.BaseHourlyRate, m => m.ConfigureMoney("base_hourly_rate"));
            price.ComplexProperty(p => p.HallRental, m => m.ConfigureMoney("hall_rental"));
            price.ComplexProperty(p => p.MembershipDiscount, m => m.ConfigureMoney("membership_discount"));
            price.ComplexProperty(p => p.ExtrasTotal, m => m.ConfigureMoney("extras_total"));
            price.ComplexProperty(p => p.Total, m => m.ConfigureMoney("total"));
        });
        builder.OwnsMany(x => x.ExtraServices, line =>
        {
            line.ToTable("quote_extra_services");
            line.WithOwner().HasForeignKey("QuoteId");
            line.Property<int>("Id");
            line.HasKey("Id");
            line.Property(l => l.Code).HasMaxLength(30);
            line.Property(l => l.Name).HasMaxLength(100);
            line.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
            line.Property(l => l.Quantity);
            line.OwnsOne(l => l.UnitCost, m => m.ConfigureMoney("unit_cost"));
            line.Navigation(l => l.UnitCost).IsRequired();
            line.Ignore(l => l.LineTotal);
        });
        builder.Navigation(x => x.ExtraServices).HasField("_extraServices").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
    }
}
