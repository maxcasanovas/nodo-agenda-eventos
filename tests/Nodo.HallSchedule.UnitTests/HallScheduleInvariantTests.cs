using Nodo.HallSchedule.Domain;
using DomainException = Nodo.SharedKernel.DomainException; // avoids importing SharedKernel.InvariantCodes
using Schedule = Nodo.HallSchedule.Domain.HallSchedule;
using static Nodo.HallSchedule.UnitTests.TestData;

namespace Nodo.HallSchedule.UnitTests;

public class HallScheduleInvariantTests
{
    // ---- INV-01 / BR-01: TimeSlot duration (the slot is also a VO of this module) ----

    [Theory]
    [InlineData(9, 10)] // 1 h: lower bound
    [InlineData(9, 17)] // 8 h: upper bound
    [InlineData(10, 14)]
    public void TimeSlotCreate_Inv01_DurationWithinRange_Succeeds(int start, int end)
    {
        var slot = Slot(start, end);

        slot.DurationHours.ShouldBe(end - start);
    }

    [Theory]
    [InlineData(9, 18)]  // 9 h: just above the limit
    [InlineData(9, 9)]   // 0 h
    [InlineData(12, 10)] // ends before it starts
    public void TimeSlotCreate_Inv01_DurationOutOfRange_ThrowsDurationOutOfRange(int start, int end)
    {
        var ex = Should.Throw<DomainException>(() => Slot(start, end));

        ex.Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");
    }

    [Theory]
    [InlineData(9, 30, 11, 0)]
    [InlineData(9, 0, 11, 30)]
    public void TimeSlotCreate_Inv01_NotExactHour_ThrowsDurationOutOfRange(int sh, int sm, int eh, int em)
    {
        var ex = Should.Throw<DomainException>(
            () => TimeSlot.Create(Day, new TimeOnly(sh, sm), new TimeOnly(eh, em)));

        ex.Code.ShouldBe(InvariantCodes.DurationOutOfRange);
    }

    // ---- INV-18 / BR-03: no overlap including 1 h buffers ----

    [Fact]
    public void PlaceProvisionalBlock_Inv18_EmptyDay_Succeeds()
    {
        var schedule = NewSchedule();

        PlaceProvisional(schedule, EventId.New(), 10, 12);

        schedule.ActiveBlocks.Count().ShouldBe(1);
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_DirectOverlap_ThrowsSlotOverlap()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 14);

        var ex = Should.Throw<DomainException>(() => PlaceProvisional(schedule, EventId.New(), 12, 15));

        ex.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_OverlapOnlyThroughBuffer_ThrowsSlotOverlap()
    {
        // BR-03: A 10-12 occupies 09-13 with buffers; B 13-15 needs 12-16 -> conflict.
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 12);

        var ex = Should.Throw<DomainException>(() => PlaceProvisional(schedule, EventId.New(), 13, 15));

        ex.Code.ShouldBe(InvariantCodes.SlotOverlap);
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_ExactlyTwoHoursApart_Succeeds()
    {
        // Limit: A 10-12 (09-13) and B 14-16 (13-17) only touch at 13:00.
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 12);

        PlaceProvisional(schedule, EventId.New(), 14, 16);

        schedule.ActiveBlocks.Count().ShouldBe(2);
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_NewEventBeforeExistingExactlyTwoHoursApart_Succeeds()
    {
        // Limit in the other direction: B 10-12 (09-13) before A 14-16 (13-17) only touch at 13:00.
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 14, 16);

        PlaceProvisional(schedule, EventId.New(), 10, 12);

        schedule.ActiveBlocks.Count().ShouldBe(2);
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_EventBeforeExistingWithOneHourGap_ThrowsSlotOverlap()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 14, 16);

        var ex = Should.Throw<DomainException>(() => PlaceProvisional(schedule, EventId.New(), 11, 13));

        ex.Code.ShouldBe(InvariantCodes.SlotOverlap);
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv18_ReleasedBlockDoesNotBlock()
    {
        var schedule = NewSchedule();
        var first = EventId.New();
        PlaceProvisional(schedule, first, 10, 14);
        schedule.Release(first, NowUtc);

        PlaceProvisional(schedule, EventId.New(), 10, 14);

        schedule.ActiveBlocks.Count().ShouldBe(1);
    }

    [Fact]
    public void PlaceDefinitiveBlock_Inv18_OverlapWithProvisional_ThrowsSlotOverlap()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 14);

        var ex = Should.Throw<DomainException>(() =>
            schedule.PlaceDefinitiveBlock(EventId.New(), Slot(11, 13), NoEquipment, Inventory, NowUtc));

        ex.Code.ShouldBe(InvariantCodes.SlotOverlap);
    }

    // ---- INV-19: at most one active block per event ----

    [Fact]
    public void PlaceProvisionalBlock_Inv19_SecondActiveBlockForSameEvent_ThrowsDuplicateBlock()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 9, 11);

        var ex = Should.Throw<DomainException>(() => PlaceProvisional(schedule, eventId, 15, 17));

        ex.Code.ShouldBe("SCHEDULE.DUPLICATE_BLOCK");
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv19_SameEventAfterRelease_Succeeds()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 9, 11);
        schedule.Release(eventId, NowUtc);

        PlaceProvisional(schedule, eventId, 15, 17);

        schedule.ActiveBlocks.Count().ShouldBe(1);
        schedule.Blocks.Count.ShouldBe(2);
    }

    // ---- INV-20 / BR-08: equipment never above stock ----

    [Theory]
    [InlineData("PROJECTOR", 1)] // exactly the stock
    [InlineData("TV", 2)]
    public void PlaceProvisionalBlock_Inv20_EquipmentUpToStock_Succeeds(string code, int quantity)
    {
        var schedule = NewSchedule();

        schedule.PlaceProvisionalBlock(
            EventId.New(), Slot(10, 12), [EquipmentAllocation.Create(code, quantity)], Inventory, NowUtc);

        schedule.ActiveBlocks.Single().EquipmentAllocations.Single().Quantity.ShouldBe(quantity);
    }

    [Theory]
    [InlineData("PROJECTOR", 2)] // one above the stock
    [InlineData("TV", 3)]
    [InlineData("SMOKE_MACHINE", 1)] // not in the inventory
    public void PlaceProvisionalBlock_Inv20_EquipmentAboveStock_ThrowsEquipmentUnavailable(string code, int quantity)
    {
        var schedule = NewSchedule();

        var ex = Should.Throw<DomainException>(() => schedule.PlaceProvisionalBlock(
            EventId.New(), Slot(10, 12), [EquipmentAllocation.Create(code, quantity)], Inventory, NowUtc));

        ex.Code.ShouldBe("SCHEDULE.EQUIPMENT_UNAVAILABLE");
    }

    [Fact]
    public void PlaceProvisionalBlock_Inv20_SameCodeSplitInTwoLines_SumsAgainstStock()
    {
        var schedule = NewSchedule();
        EquipmentAllocation[] lines = [EquipmentAllocation.Create("TV", 2), EquipmentAllocation.Create("TV", 1)];

        var ex = Should.Throw<DomainException>(
            () => schedule.PlaceProvisionalBlock(EventId.New(), Slot(10, 12), lines, Inventory, NowUtc));

        ex.Code.ShouldBe(InvariantCodes.EquipmentUnavailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EquipmentAllocationCreate_Inv20_NonPositiveQuantity_ThrowsEquipmentUnavailable(int quantity)
    {
        var ex = Should.Throw<DomainException>(() => EquipmentAllocation.Create("TV", quantity));

        ex.Code.ShouldBe(InvariantCodes.EquipmentUnavailable);
    }

    // ---- Unchanged state after a rejected command ----

    [Fact]
    public void PlaceProvisionalBlock_AfterDomainException_LeavesAggregateUntouchedAndRaisesNoEvents()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 14);
        schedule.DequeueDomainEvents();

        Should.Throw<DomainException>(() => PlaceProvisional(schedule, EventId.New(), 12, 15));
        Should.Throw<DomainException>(() => schedule.PlaceProvisionalBlock(
            EventId.New(), Slot(18, 20), [EquipmentAllocation.Create("PROJECTOR", 5)], Inventory, NowUtc));

        schedule.Blocks.Count.ShouldBe(1);
        schedule.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void PlaceProvisionalBlock_SlotOfAnotherDay_IsRejectedAsProgrammingError()
    {
        var schedule = NewSchedule();
        var otherDay = TimeSlot.Create(Day.AddDays(1), new TimeOnly(10, 0), new TimeOnly(12, 0));

        Should.Throw<ArgumentException>(
            () => schedule.PlaceProvisionalBlock(EventId.New(), otherDay, NoEquipment, Inventory, NowUtc));
    }
}
