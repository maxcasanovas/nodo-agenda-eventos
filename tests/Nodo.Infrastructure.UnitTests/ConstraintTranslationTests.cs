using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nodo.BuildingBlocks.Infrastructure.Persistence;

namespace Nodo.Infrastructure.UnitTests;

/// <summary>
/// The database is defense in depth: a violated constraint (concurrent request, direct write) must surface as a 409
/// carrying the same code the domain would have returned with a 422.
/// </summary>
public class ConstraintTranslationTests
{
    private static PostgresException Violation(string sqlState, string? constraint) => new(
        messageText: "violation", severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState, constraintName: constraint);

    [Theory]
    [InlineData("23514", "ck_hall_events_inv04_attendance", "EVENT.ATTENDANCE_OUT_OF_RANGE")]
    [InlineData("23514", "ck_hall_events_inv02_deposit_percentage", "EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE")]
    [InlineData("23514", "ck_hall_events_inv01_duration", "EVENT.DURATION_OUT_OF_RANGE")]
    [InlineData("23514", "ck_hall_events_inv05_credit_generated", "EVENT.CANCELLATION_NOT_ALLOWED")]
    [InlineData("23514", "ck_customer_credits_inv21_balance_non_negative", "CREDIT.INSUFFICIENT")]
    [InlineData("23514", "ck_quotes_inv14_validity_96h", "QUOTE.EXPIRED")]
    [InlineData("23514", "ck_quotes_inv17_membership_discount", "QUOTE.INVALID_DISCOUNT")]
    public void EventLifecycleAndQuotes_CheckViolation_BecomesConflictWithTheInvariantCode(
        string sqlState, string constraint, string expectedCode)
    {
        var map = constraint.Contains("quotes")
            ? Nodo.Quotes.Infrastructure.ConstraintCodes.Map
            : Nodo.EventLifecycle.Infrastructure.ConstraintCodes.Map;

        var conflict = map.Translate(Violation(sqlState, constraint));

        conflict.ShouldNotBeNull();
        conflict.Code.ShouldBe(expectedCode);
        conflict.Message.ShouldContain(expectedCode);
    }

    [Fact]
    public void HallSchedule_ExclusionViolation_BecomesSlotOverlap()
    {
        var conflict = Nodo.HallSchedule.Infrastructure.ConstraintCodes.Map
            .Translate(Violation("23P01", "ex_schedule_blocks_inv18_overlap"));

        conflict!.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
    }

    [Fact]
    public void HallSchedule_UniqueViolationOfTheActiveBlockIndex_BecomesDuplicateBlock()
    {
        var conflict = Nodo.HallSchedule.Infrastructure.ConstraintCodes.Map
            .Translate(Violation("23505", "ux_schedule_blocks_inv19_one_active_block_per_event"));

        conflict!.Code.ShouldBe("SCHEDULE.DUPLICATE_BLOCK");
    }

    [Fact]
    public void ConstraintWithoutAnInvariantId_IsStillAConflictButWithoutCode()
    {
        var conflict = Nodo.Quotes.Infrastructure.ConstraintCodes.Map.Translate(Violation("23505", "pk_quotes"));

        conflict.ShouldNotBeNull();
        conflict.Code.ShouldBeNull();
    }

    [Fact]
    public void InvariantOfAnotherModule_IsNotMappedByThisOne()
    {
        // INV-18 belongs to HallSchedule: Quotes does not know it, so it must not invent a code for it.
        var conflict = Nodo.Quotes.Infrastructure.ConstraintCodes.Map.Translate(Violation("23P01", "ex_something_inv18_overlap"));

        conflict!.Code.ShouldBeNull();
    }

    [Theory]
    [InlineData("40001")] // serialization failure
    [InlineData("23503")] // foreign key violation is a bug, not an invariant
    [InlineData("08006")] // connection failure
    public void OtherDatabaseErrors_AreNotTranslated(string sqlState) =>
        Nodo.Quotes.Infrastructure.ConstraintCodes.Map.Translate(Violation(sqlState, "ck_quotes_inv04_attendance")).ShouldBeNull();

    [Fact]
    public void DbUpdateException_WrappingAConstraintViolation_IsTranslated()
    {
        var wrapped = new DbUpdateException("save failed", Violation("23514", "ck_quotes_inv04_attendance"));

        Nodo.Quotes.Infrastructure.ConstraintCodes.Map.Translate(wrapped)!.Code.ShouldBe("EVENT.ATTENDANCE_OUT_OF_RANGE");
    }

    [Fact]
    public void DbUpdateException_WrappingSomethingElse_IsNotTranslated() =>
        Nodo.Quotes.Infrastructure.ConstraintCodes.Map.Translate(new DbUpdateException("x", new InvalidOperationException())).ShouldBeNull();
}
