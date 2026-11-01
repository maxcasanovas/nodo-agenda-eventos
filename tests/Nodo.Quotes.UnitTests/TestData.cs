using Nodo.Quotes.Domain;
using Nodo.SharedKernel;

namespace Nodo.Quotes.UnitTests;

internal static class TestData
{
    public static readonly DateTime NowUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 11, 1); // local date at NowUtc (08:00 in La Paz)
    public static readonly DateOnly EventDay = Today.AddDays(10);
    public static readonly CustomerId Customer = new(Guid.Parse("22222222-0000-0000-0000-000000000001"));
    public static readonly HallId Hall = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    public static readonly IReadOnlyDictionary<string, int> Stock = new Dictionary<string, int>
    {
        ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1
    };

    public static TimeSlot Slot(int startHour, int endHour, DateOnly? date = null) =>
        TimeSlot.Create(date ?? EventDay, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    public static ExtraServiceLine Equipment(string code, string name, decimal unitCost, int quantity = 1) =>
        ExtraServiceLine.Create(code, name, ExtraServiceType.OwnEquipment, quantity, Money.Of(unitCost));

    public static ExtraServiceLine Catering(string code, string name, decimal perPerson, int people) =>
        ExtraServiceLine.Create(code, name, ExtraServiceType.ExternalCatering, people, Money.Of(perPerson));

    /// <summary>Issues a valid quote (4 h, 40 people, no extras, no discount) unless overridden.</summary>
    public static Quote Issue(
        TimeSlot? slot = null,
        int attendance = 40,
        IReadOnlyCollection<ExtraServiceLine>? extras = null,
        int discount = 0,
        DateTime? nowUtc = null,
        DateOnly? today = null) =>
        Quote.Issue(
            Customer, Hall, slot ?? Slot(9, 13), Attendance.Create(attendance), extras ?? [], Stock,
            discount, nowUtc ?? NowUtc, today ?? Today);
}
