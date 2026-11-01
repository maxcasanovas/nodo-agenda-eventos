using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodo.SharedKernel;

namespace Nodo.BuildingBlocks.Infrastructure.Persistence;

public static class MappingExtensions
{
    /// <summary>
    /// Stores a <see cref="Money"/> as <c>{prefix}_amount numeric(12,2)</c> and <c>{prefix}_currency varchar(3)</c>.
    /// Mapped as a complex type: a value object has no identity, so the same instance can safely be referenced from
    /// several places of the model (an owned entity is tracked by identity and breaks when shared).
    /// </summary>
    public static ComplexPropertyBuilder<Money> ConfigureMoney(this ComplexPropertyBuilder<Money> money, string prefix)
    {
        money.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(12, 2);
        money.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3);
        return money;
    }

    /// <summary>
    /// Same columns for a Money inside an owned collection item (EF cannot nest complex types in owned entities).
    /// Every item creates its own Money, so there is nothing to share there.
    /// </summary>
    public static OwnedNavigationBuilder<TOwner, Money> ConfigureMoney<TOwner>(
        this OwnedNavigationBuilder<TOwner, Money> money, string prefix)
        where TOwner : class
    {
        money.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(12, 2);
        money.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3);
        return money;
    }
}