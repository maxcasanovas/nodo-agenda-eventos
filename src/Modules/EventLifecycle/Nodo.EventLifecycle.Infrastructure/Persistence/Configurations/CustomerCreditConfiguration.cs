using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodo.BuildingBlocks.Infrastructure.Persistence;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Infrastructure.Persistence.Configurations;

internal sealed class CustomerCreditConfiguration : IEntityTypeConfiguration<CustomerCredit>
{
    public void Configure(EntityTypeBuilder<CustomerCredit> builder)
    {
        // INV-21: the balance is never negative. There is deliberately no column or table for refunds (INV-22).
        builder.ToTable("customer_credits", t =>
            t.HasCheckConstraint("ck_customer_credits_inv21_balance_non_negative", "balance_amount >= 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value)).ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.ComplexProperty(x => x.Balance, m => m.ConfigureMoney("balance"));

        builder.OwnsMany(x => x.Movements, movement =>
        {
            movement.ToTable("customer_credit_movements");
            movement.WithOwner().HasForeignKey("CustomerId");
            movement.Property<int>("Id");
            movement.HasKey("Id");
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.EventId).HasConversion(id => id.Value, value => new HallEventId(value));
            movement.Property(m => m.Reason).HasMaxLength(200);
            movement.Property(m => m.OccurredAtUtc);
            movement.OwnsOne(m => m.Amount, m => m.ConfigureMoney("amount"));
            movement.Navigation(m => m.Amount).IsRequired();
        });
        builder.Navigation(x => x.Movements).HasField("_movements").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
