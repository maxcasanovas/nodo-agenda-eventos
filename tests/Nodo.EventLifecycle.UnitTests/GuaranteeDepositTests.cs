using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.TestData;

namespace Nodo.EventLifecycle.UnitTests;

/// <summary>BR-14 / BR-15 on the GuaranteeDeposit value object (INV-02, INV-09, INV-10).</summary>
public class GuaranteeDepositTests
{
    [Fact]
    public void Create_Br14_RequiredIsTotalTimesPercentage()
    {
        var deposit = GuaranteeDeposit.Create(30, Money.Of(2800m));

        deposit.Required.Amount.ShouldBe(840m);
        deposit.Accredited.Amount.ShouldBe(0m);
        deposit.IsCovered.ShouldBeFalse();
    }

    [Fact]
    public void Create_Br14_RequiredIsRoundedAwayFromZero()
    {
        // 33.33 * 25 % = 8.3325 -> 8.33 ; 10.10 * 25 % = 2.525 -> 2.53
        GuaranteeDeposit.Create(25, Money.Of(33.33m)).Required.Amount.ShouldBe(8.33m);
        GuaranteeDeposit.Create(25, Money.Of(10.10m)).Required.Amount.ShouldBe(2.53m);
    }

    [Fact]
    public void Accredit_Inv09_ReturnsNewInstanceAndLeavesTheOriginalUntouched()
    {
        var original = GuaranteeDeposit.Create(30, Total);

        var result = original.Accredit(Bob(100m), "TX-1");

        result.Deposit.ShouldNotBeSameAs(original);
        original.Accredited.Amount.ShouldBe(0m);
        original.TransactionIds.ShouldBeEmpty();
        result.Deposit.Accredited.Amount.ShouldBe(100m);
        result.Applied.Amount.ShouldBe(100m);
        result.Excess.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Accredit_Inv09_SameTransactionId_AppliesNothingTheSecondTime()
    {
        var first = GuaranteeDeposit.Create(30, Total).Accredit(Bob(100m), "TX-1").Deposit;

        var second = first.Accredit(Bob(100m), "TX-1");

        second.Deposit.ShouldBeSameAs(first);
        second.Applied.Amount.ShouldBe(0m);
        second.Deposit.Accredited.Amount.ShouldBe(100m);
    }

    [Fact]
    public void Accredit_Br15_PaymentsAccumulateUntilCovered()
    {
        var deposit = GuaranteeDeposit.Create(30, Total); // 600

        deposit = deposit.Accredit(Bob(200m), "A").Deposit;
        deposit = deposit.Accredit(Bob(200m), "B").Deposit;
        deposit.IsCovered.ShouldBeFalse();
        deposit = deposit.Accredit(Bob(200m), "C").Deposit;

        deposit.IsCovered.ShouldBeTrue();
        deposit.Accredited.Amount.ShouldBe(600m);
        deposit.TransactionIds.ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void Accredit_Br15_Overpayment_SplitsAppliedAndExcess()
    {
        var deposit = GuaranteeDeposit.Create(30, Total).Accredit(Bob(500m), "A").Deposit;

        var result = deposit.Accredit(Bob(250m), "B");

        result.Applied.Amount.ShouldBe(100m);
        result.Excess.Amount.ShouldBe(150m);
        result.Deposit.Accredited.Amount.ShouldBe(600m);
    }

    [Fact]
    public void Accredit_Inv10_OtherCurrency_ThrowsCurrencyMismatch() =>
        Should.Throw<DomainException>(() =>
                GuaranteeDeposit.Create(30, Total).Accredit(Money.Of(100m, "USD"), "TX"))
            .Code.ShouldBe("MONEY.CURRENCY_MISMATCH");

    [Fact]
    public void Equals_SameStructure_AreEqual()
    {
        var a = GuaranteeDeposit.Create(30, Total).Accredit(Bob(100m), "TX-1").Deposit;
        var b = GuaranteeDeposit.Create(30, Total).Accredit(Bob(100m), "TX-1").Deposit;

        a.ShouldBe(b);
    }
}
