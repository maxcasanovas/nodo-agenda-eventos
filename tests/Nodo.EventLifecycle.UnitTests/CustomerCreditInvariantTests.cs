using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.TestData;

namespace Nodo.EventLifecycle.UnitTests;

/// <summary>INV-21 / INV-22 and BR-22: customer credit.</summary>
public class CustomerCreditInvariantTests
{
    private static readonly HallEventId SomeEvent = HallEventId.New();

    private static CustomerCredit CreditWith(decimal amount)
    {
        var credit = CustomerCredit.Open(Customer);
        credit.Grant(Bob(amount), SomeEvent, "Cancelación", NowUtc);
        credit.DequeueDomainEvents();
        return credit;
    }

    [Fact]
    public void Open_NewCredit_StartsAtZeroForTheCustomer()
    {
        var credit = CustomerCredit.Open(Customer);

        credit.Id.ShouldBe(Customer);
        credit.Balance.Amount.ShouldBe(0m);
        credit.Movements.ShouldBeEmpty();
    }

    [Fact]
    public void Grant_Br22_AddsToBalanceAndRecordsMovement()
    {
        var credit = CustomerCredit.Open(Customer);

        credit.Grant(Bob(600m), SomeEvent, "Cancelación del evento", NowUtc);
        credit.Grant(Bob(150m), SomeEvent, "Excedente de depósito", NowUtc);

        credit.Balance.Amount.ShouldBe(750m);
        credit.Movements.Count.ShouldBe(2);
        credit.Movements.ShouldAllBe(m => m.Type == CreditMovementType.Granted);
        credit.DequeueDomainEvents().OfType<CustomerCreditGranted>().Last().Balance.Amount.ShouldBe(750m);
    }

    [Fact]
    public void Grant_ZeroAmount_IsIgnored()
    {
        var credit = CustomerCredit.Open(Customer);

        credit.Grant(Bob(0m), SomeEvent, "Sin saldo", NowUtc);

        credit.Balance.Amount.ShouldBe(0m);
        credit.Movements.ShouldBeEmpty();
        credit.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Apply_Inv21_AmountBelowBalance_AppliesWhatWasAsked()
    {
        var credit = CreditWith(600m);

        var applied = credit.Apply(Bob(400m), SomeEvent, NowUtc);

        applied.Amount.ShouldBe(400m);
        credit.Balance.Amount.ShouldBe(200m);
    }

    [Fact]
    public void Apply_Inv21_AmountEqualToBalance_LeavesZero()
    {
        var credit = CreditWith(600m);

        credit.Apply(Bob(600m), SomeEvent, NowUtc).Amount.ShouldBe(600m);

        credit.Balance.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Apply_Inv21_AmountAboveBalance_AppliesOnlyWhatIsAvailable()
    {
        var credit = CreditWith(600m);

        var applied = credit.Apply(Bob(600.01m), SomeEvent, NowUtc);

        applied.Amount.ShouldBe(600m);
        credit.Balance.Amount.ShouldBe(0m); // never negative
        credit.Movements.Last().Type.ShouldBe(CreditMovementType.Applied);
        credit.DequeueDomainEvents().OfType<CustomerCreditApplied>().Single().Amount.Amount.ShouldBe(600m);
    }

    [Fact]
    public void Apply_Inv21_NoBalance_ThrowsCreditInsufficientAndChangesNothing()
    {
        var credit = CustomerCredit.Open(Customer);

        Should.Throw<DomainException>(() => credit.Apply(Bob(100m), SomeEvent, NowUtc))
            .Code.ShouldBe("CREDIT.INSUFFICIENT");

        credit.Balance.Amount.ShouldBe(0m);
        credit.Movements.ShouldBeEmpty();
        credit.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Apply_Inv21_ZeroRequested_ThrowsCreditInsufficientAndKeepsBalance()
    {
        var credit = CreditWith(600m);

        Should.Throw<DomainException>(() => credit.Apply(Bob(0m), SomeEvent, NowUtc))
            .Code.ShouldBe("CREDIT.INSUFFICIENT");

        credit.Balance.Amount.ShouldBe(600m);
        credit.Movements.Count.ShouldBe(1);
        credit.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Grant_Inv10_DifferentCurrency_ThrowsCurrencyMismatchAndKeepsBalance()
    {
        var credit = CreditWith(600m);

        Should.Throw<DomainException>(() => credit.Grant(Money.Of(10m, "USD"), SomeEvent, "x", NowUtc))
            .Code.ShouldBe("MONEY.CURRENCY_MISMATCH");

        credit.Balance.Amount.ShouldBe(600m);
        credit.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void CustomerCredit_Inv22_ExposesNoOperationToWithdrawOrRefund()
    {
        var publicMethods = typeof(CustomerCredit)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToHashSet();

        // Credit can only be opened, granted and applied to a reschedule. Nothing converts it to money.
        publicMethods.ShouldBe(["Open", "Grant", "Apply"], ignoreOrder: true);
    }

    [Fact]
    public void Movements_ExposedAsReadOnly()
    {
        var credit = CreditWith(600m);

        (credit.Movements is List<CreditMovement>).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => ((IList<CreditMovement>)credit.Movements).Clear());
    }
}
