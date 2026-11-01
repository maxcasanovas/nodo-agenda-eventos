using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;
using static Nodo.HallSchedule.UnitTests.UseCases.HsTestHost;

namespace Nodo.HallSchedule.UnitTests.UseCases;

/// <summary>IHallScheduleModuleApi: the synchronous API that Quotes and EventLifecycle call.</summary>
public class ModuleApiTests
{
    private static readonly Guid Event1 = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid Event2 = Guid.Parse("44444444-0000-0000-0000-000000000002");

    [Fact]
    public async Task PlaceProvisionalBlock_FreeSlot_SucceedsAndPersistsWithOneCommit()
    {
        using var host = new HsTestHost();

        var result = await host.PlaceProvisionalAsync(Event1, 10, 14);

        result.Success.ShouldBeTrue();
        host.UnitOfWork.SaveCount.ShouldBe(1);
        host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.Single().Kind.ShouldBe(BlockKind.Provisional);
    }

    [Fact]
    public async Task PlaceProvisionalBlock_Overlap_FailsWithCatalogCodeAndSavesNothing()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 10, 12);
        host.Outbox.Clear();
        var commitsBefore = host.UnitOfWork.SaveCount;

        var result = await host.PlaceProvisionalAsync(Event2, 13, 15); // only conflicts through the buffer (INV-18)

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        host.UnitOfWork.SaveCount.ShouldBe(commitsBefore);
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlaceProvisionalBlock_SameEventTwice_FailsWithDuplicateBlock()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 9, 11);

        var result = await host.PlaceProvisionalAsync(Event1, 15, 17);

        result.Code.ShouldBe("SCHEDULE.DUPLICATE_BLOCK");
    }

    [Fact]
    public async Task PlaceProvisionalBlock_EquipmentAboveStock_FailsWithEquipmentUnavailable()
    {
        using var host = new HsTestHost();

        var result = await host.PlaceProvisionalAsync(Event1, 10, 14, new EquipmentRequest("PROJECTOR", 2));

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe("SCHEDULE.EQUIPMENT_UNAVAILABLE");
        host.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task PlaceProvisionalBlock_EquipmentWithinStock_ReservesIt()
    {
        using var host = new HsTestHost();

        var result = await host.PlaceProvisionalAsync(
            Event1, 10, 14, new EquipmentRequest("PROJECTOR", 1), new EquipmentRequest("TV", 2));

        result.Success.ShouldBeTrue();
        var block = host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.Single();
        block.EquipmentAllocations.Select(e => (e.Code, e.Quantity)).ShouldBe([("PROJECTOR", 1), ("TV", 2)], ignoreOrder: true);
    }

    [Fact]
    public async Task PlaceProvisionalBlock_InvalidDuration_FailsWithDurationCodeInsteadOfThrowing()
    {
        using var host = new HsTestHost();

        var result = await host.PlaceProvisionalAsync(Event1, 9, 18); // 9 h

        result.Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");
        host.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task PlaceDefinitiveBlock_FreeSlot_CreatesDefinitiveBlock()
    {
        using var host = new HsTestHost();

        var result = await host.Api.PlaceDefinitiveBlockAsync(
            Event1, HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), []);

        result.Success.ShouldBeTrue();
        host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.Single().Kind.ShouldBe(BlockKind.Definitive);
    }

    [Fact]
    public async Task PlaceDefinitiveBlock_Overlap_FailsWithSlotOverlap()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 10, 14);

        var result = await host.Api.PlaceDefinitiveBlockAsync(
            Event2, HallGuid, Day, new TimeOnly(12, 0), new TimeOnly(15, 0), []);

        result.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
    }

    [Fact]
    public async Task CheckAvailability_FreeSlot_IsSuccessfulAndPersistsNothing()
    {
        using var host = new HsTestHost();

        var result = await host.Api.CheckAvailabilityAsync(
            HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), []);

        result.Success.ShouldBeTrue();
        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Schedules.Contains(new HallId(HallGuid), Day).ShouldBeFalse();
    }

    [Fact]
    public async Task CheckAvailability_TakenSlot_ReportsOverlapWithoutChangingAnything()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 10, 14);
        var commits = host.UnitOfWork.SaveCount;

        var result = await host.Api.CheckAvailabilityAsync(
            HallGuid, Day, new TimeOnly(12, 0), new TimeOnly(16, 0), []);

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
    }

    [Fact]
    public async Task CheckAvailability_ExcludingTheEventsOwnBlock_IsAvailable()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 10, 14);

        var result = await host.Api.CheckAvailabilityAsync(
            HallGuid, Day, new TimeOnly(11, 0), new TimeOnly(15, 0), [], excludeEventId: Event1);

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckAvailability_EquipmentAboveStock_ReportsEquipmentUnavailable()
    {
        using var host = new HsTestHost();

        var result = await host.Api.CheckAvailabilityAsync(
            HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), [new EquipmentRequest("SOUND", 2)]);

        result.Code.ShouldBe("SCHEDULE.EQUIPMENT_UNAVAILABLE");
    }

    [Fact]
    public async Task CheckAvailability_UsesTheInventoryFromTheSpacesCatalog()
    {
        using var host = new HsTestHost();
        host.Spaces.Stock = new Dictionary<string, int> { ["PROJECTOR"] = 3 };

        var result = await host.Api.CheckAvailabilityAsync(
            HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), [new EquipmentRequest("PROJECTOR", 3)]);

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Release_ThenPlaceAgain_TheSlotIsFreeAgain()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(Event1, 10, 14);
        var schedule = host.Schedules.Peek(new HallId(HallGuid), Day)!;
        schedule.Release(new EventId(Event1), host.Clock.UtcNow);

        var result = await host.PlaceProvisionalAsync(Event2, 10, 14);

        result.Success.ShouldBeTrue();
    }
}
