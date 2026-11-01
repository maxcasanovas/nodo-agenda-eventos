namespace Nodo.SharedKernel.UnitTests;

public class NodoTimeZoneTests
{
    [Fact]
    public void ToLocal_UtcMidnight_IsFourHoursEarlierPreviousDay()
    {
        var local = NodoTimeZone.ToLocal(new DateTime(2026, 3, 10, 2, 0, 0, DateTimeKind.Utc));

        local.ShouldBe(new DateTime(2026, 3, 9, 22, 0, 0));
    }

    [Fact]
    public void ToUtc_LocalDateAndTime_AddsFourHours()
    {
        var utc = NodoTimeZone.ToUtc(new DateOnly(2026, 3, 9), new TimeOnly(22, 0));

        utc.ShouldBe(new DateTime(2026, 3, 10, 2, 0, 0));
        utc.Kind.ShouldBe(DateTimeKind.Utc);
    }
}
