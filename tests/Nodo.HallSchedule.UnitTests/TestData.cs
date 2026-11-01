using Nodo.HallSchedule.Domain;
using Schedule = Nodo.HallSchedule.Domain.HallSchedule; // "HallSchedule" alone resolves to the namespace

namespace Nodo.HallSchedule.UnitTests;

internal static class TestData
{
    public static readonly HallId Hall = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly DateOnly Day = new(2026, 11, 10);
    public static readonly DateTime NowUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyDictionary<string, int> Inventory = new Dictionary<string, int>
    {
        ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1
    };

    public static readonly IReadOnlyCollection<EquipmentAllocation> NoEquipment = [];

    public static Schedule NewSchedule() => Schedule.Create(Hall, Day);

    public static TimeSlot Slot(int startHour, int endHour) =>
        TimeSlot.Create(Day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    public static void PlaceProvisional(Schedule schedule, EventId eventId, int startHour, int endHour) =>
        schedule.PlaceProvisionalBlock(eventId, Slot(startHour, endHour), NoEquipment, Inventory, NowUtc);
}
