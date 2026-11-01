namespace Nodo.SharedKernel;

/// <summary>Hall local time (America/La_Paz). Business dates are local; technical timestamps are UTC.</summary>
public static class NodoTimeZone
{
    public const string Id = "America/La_Paz";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(Id);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public static DateTime ToUtc(DateOnly date, TimeOnly time) => ToUtc(date.ToDateTime(time));
}
