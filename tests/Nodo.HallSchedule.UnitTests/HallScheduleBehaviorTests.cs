using Nodo.HallSchedule.Domain;
using Schedule = Nodo.HallSchedule.Domain.HallSchedule;
using static Nodo.HallSchedule.UnitTests.TestData;

namespace Nodo.HallSchedule.UnitTests;

public class HallScheduleBehaviorTests
{
    [Fact]
    public void Create_NewSchedule_IsIdentifiedByHallAndDateAndEmpty()
    {
        var schedule = NewSchedule();

        schedule.Id.ShouldBe(new HallScheduleId(Hall, Day));
        schedule.HallId.ShouldBe(Hall);
        schedule.Date.ShouldBe(Day);
        schedule.Blocks.ShouldBeEmpty();
    }

    [Fact]
    public void PlaceProvisionalBlock_ValidSlot_CreatesActiveProvisionalBlockAndRaisesEvent()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();

        PlaceProvisional(schedule, eventId, 10, 12);

        var block = schedule.Blocks.Single();
        block.Kind.ShouldBe(BlockKind.Provisional);
        block.Status.ShouldBe(BlockStatus.Active);
        block.EventId.ShouldBe(eventId);
        var raised = schedule.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<SlotProvisionallyBlocked>();
        raised.EventId.ShouldBe(eventId);
        raised.Start.ShouldBe(new TimeOnly(10, 0));
    }

    [Fact]
    public void PlaceDefinitiveBlock_ValidSlot_CreatesDefinitiveBlock()
    {
        var schedule = NewSchedule();

        schedule.PlaceDefinitiveBlock(EventId.New(), Slot(10, 12), NoEquipment, Inventory, NowUtc);

        schedule.Blocks.Single().Kind.ShouldBe(BlockKind.Definitive);
        schedule.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<SlotBlockConsolidated>();
    }

    [Fact]
    public void Consolidate_ProvisionalBlock_BecomesDefinitive()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 10, 12);
        schedule.DequeueDomainEvents();

        schedule.Consolidate(eventId, NowUtc);

        schedule.Blocks.Single().Kind.ShouldBe(BlockKind.Definitive);
        schedule.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<SlotBlockConsolidated>();
    }

    [Fact]
    public void Consolidate_CalledTwice_IsIdempotentAndRaisesOneEvent()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 10, 12);
        schedule.DequeueDomainEvents();

        schedule.Consolidate(eventId, NowUtc);
        schedule.Consolidate(eventId, NowUtc);

        schedule.Blocks.Single().Kind.ShouldBe(BlockKind.Definitive);
        schedule.DequeueDomainEvents().Count.ShouldBe(1);
    }

    [Fact]
    public void Consolidate_EventWithoutActiveBlock_IsNoOp()
    {
        var schedule = NewSchedule();

        schedule.Consolidate(EventId.New(), NowUtc);

        schedule.Blocks.ShouldBeEmpty();
        schedule.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Release_ActiveBlock_MarksReleasedAndRaisesEvent()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 10, 12);
        schedule.DequeueDomainEvents();

        schedule.Release(eventId, NowUtc);

        var block = schedule.Blocks.Single();
        block.Status.ShouldBe(BlockStatus.Released);
        block.ReleasedAtUtc.ShouldBe(NowUtc);
        schedule.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<SlotReleased>();
    }

    [Fact]
    public void Release_CalledTwice_IsIdempotentAndRaisesOneEvent()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 10, 12);
        schedule.DequeueDomainEvents();

        schedule.Release(eventId, NowUtc);
        schedule.Release(eventId, NowUtc);

        schedule.DequeueDomainEvents().Count.ShouldBe(1);
    }

    [Fact]
    public void Release_DefinitiveBlock_IsAllowed()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        schedule.PlaceDefinitiveBlock(eventId, Slot(10, 12), NoEquipment, Inventory, NowUtc);

        schedule.Release(eventId, NowUtc);

        schedule.ActiveBlocks.ShouldBeEmpty();
    }

    [Fact]
    public void Consolidate_AfterRelease_DoesNotReviveTheBlock()
    {
        var schedule = NewSchedule();
        var eventId = EventId.New();
        PlaceProvisional(schedule, eventId, 10, 12);
        schedule.Release(eventId, NowUtc);

        schedule.Consolidate(eventId, NowUtc);

        schedule.Blocks.Single().Status.ShouldBe(BlockStatus.Released);
        schedule.Blocks.Single().Kind.ShouldBe(BlockKind.Provisional);
    }

    [Fact]
    public void AvailabilityChecker_Check_ReportsOverlapWithoutMutating()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 12);
        schedule.DequeueDomainEvents();

        var result = AvailabilityChecker.Check(schedule, Slot(13, 15), NoEquipment, Inventory);

        result.IsAvailable.ShouldBeFalse();
        result.Code.ShouldBe(InvariantCodes.SlotOverlap);
        schedule.Blocks.Count.ShouldBe(1);
        schedule.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void AvailabilityChecker_Check_ExcludedEventDoesNotConflictWithItself()
    {
        var schedule = NewSchedule();
        var moving = EventId.New();
        PlaceProvisional(schedule, moving, 10, 12);

        AvailabilityChecker.Check(schedule, Slot(11, 13), NoEquipment, Inventory).IsAvailable.ShouldBeFalse();
        AvailabilityChecker.Check(schedule, Slot(11, 13), NoEquipment, Inventory, moving).IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void AvailabilityChecker_Check_ExcludingOneEventStillConflictsWithOthers()
    {
        var schedule = NewSchedule();
        var other = EventId.New();
        PlaceProvisional(schedule, other, 10, 12);

        AvailabilityChecker.Check(schedule, Slot(11, 13), NoEquipment, Inventory, EventId.New())
            .IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void AvailabilityChecker_Check_FreeSlotIsAvailable()
    {
        var result = AvailabilityChecker.Check(NewSchedule(), Slot(10, 12), NoEquipment, Inventory);

        result.IsAvailable.ShouldBeTrue();
        result.Code.ShouldBeNull();
    }

    [Fact]
    public void Blocks_ExposedAsReadOnly_CannotBeModifiedFromOutside()
    {
        var schedule = NewSchedule();
        PlaceProvisional(schedule, EventId.New(), 10, 12);

        // A read-only wrapper: casting back to the mutable List must not be possible.
        (schedule.Blocks is List<ScheduleBlock>).ShouldBeFalse();
        (schedule.Blocks.Single().EquipmentAllocations is List<EquipmentAllocation>).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => ((IList<ScheduleBlock>)schedule.Blocks).Clear());
    }
}
