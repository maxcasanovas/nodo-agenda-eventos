using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

public enum QuoteStatus
{
    Issued,
    Accepted,
    Expired
}

/// <summary>
/// Quote with degressive pricing, valid for 96 h (BR-09). It does not block the calendar (BR-10).
/// Once accepted, price and services are frozen: no method changes them (INV-15).
/// </summary>
public sealed class Quote : AggregateRoot<QuoteId>
{
    public const int ValidityHours = 96;

    private readonly List<ExtraServiceLine> _extraServices = [];

    private Quote() : base(default)
    {
        Slot = null!;
        Attendance = null!;
        Price = null!;
    }

    private Quote(
        QuoteId id, CustomerId customerId, HallId hallId, TimeSlot slot, Attendance attendance,
        IEnumerable<ExtraServiceLine> extraServices, PriceBreakdown price, DateTime issuedAtUtc) : base(id)
    {
        CustomerId = customerId;
        HallId = hallId;
        Slot = slot;
        Attendance = attendance;
        _extraServices.AddRange(extraServices);
        Price = price;
        Status = QuoteStatus.Issued;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = issuedAtUtc.AddHours(ValidityHours);
    }

    public CustomerId CustomerId { get; private set; }

    public HallId HallId { get; private set; }

    public TimeSlot Slot { get; private set; }

    public Attendance Attendance { get; private set; }

    public IReadOnlyCollection<ExtraServiceLine> ExtraServices => _extraServices.AsReadOnly();

    public PriceBreakdown Price { get; private set; }

    public QuoteStatus Status { get; private set; }

    public DateTime IssuedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? AcceptedAtUtc { get; private set; }

    /// <param name="equipmentStock">Own-equipment stock by code, supplied by the caller (BR-08).</param>
    /// <param name="membershipDiscountPercentage">0-10; 0 when the customer has no active membership (BR-07).</param>
    public static Quote Issue(
        CustomerId customerId,
        HallId hallId,
        TimeSlot slot,
        Attendance attendance,
        IReadOnlyCollection<ExtraServiceLine> extraServices,
        IReadOnlyDictionary<string, int> equipmentStock,
        int membershipDiscountPercentage,
        DateTime nowUtc,
        DateOnly todayLocal)
    {
        BookingPolicy.EnsureWithinOperatingHours(slot);                   // BR-02
        BookingPolicy.EnsureWithinBookingWindow(slot.Date, todayLocal);   // BR-05
        EnsureExtraServicesAreValid(extraServices, equipmentStock);       // BR-08

        var price = EventPricingCalculator.Calculate(slot.DurationHours, membershipDiscountPercentage, extraServices);

        var quote = new Quote(QuoteId.New(), customerId, hallId, slot, attendance, extraServices, price, nowUtc);
        quote.RaiseDomainEvent(new QuoteIssued(
            quote.Id, customerId, slot.Date, slot.Start, slot.End, price.Total, quote.ExpiresAtUtc, nowUtc));
        return quote;
    }

    public bool IsExpiredAt(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// Accepting creates the event request (BR-11), so the booking window (BR-05) must hold on the acceptance day:
    /// a quote that could not become a request cannot be accepted.
    /// </summary>
    public void Accept(DateTime nowUtc, DateOnly todayLocal)
    {
        if (Status == QuoteStatus.Expired || (Status == QuoteStatus.Issued && IsExpiredAt(nowUtc)))
        {
            throw new DomainException(InvariantCodes.Expired,
                $"La cotización venció el {ExpiresAtUtc:dd/MM/yyyy HH:mm} UTC y ya no se puede aceptar.");
        }

        if (Status != QuoteStatus.Issued)
        {
            throw new DomainException(InvariantCodes.InvalidStateTransition,
                "La cotización ya fue aceptada.");
        }

        BookingPolicy.EnsureWithinBookingWindow(Slot.Date, todayLocal);

        Status = QuoteStatus.Accepted;
        AcceptedAtUtc = nowUtc;
        RaiseDomainEvent(new QuoteAccepted(Id, CustomerId, HallId, Slot, Attendance, ExtraServices, Price, nowUtc));
    }

    /// <summary>Marks an overdue quote as expired. Idempotent; does nothing if it is not issued or not yet overdue.</summary>
    /// <returns>True if the quote changed.</returns>
    public bool Expire(DateTime nowUtc)
    {
        if (Status != QuoteStatus.Issued || !IsExpiredAt(nowUtc))
            return false;

        Status = QuoteStatus.Expired;
        RaiseDomainEvent(new QuoteExpired(Id, CustomerId, nowUtc));
        return true;
    }

    private static void EnsureExtraServicesAreValid(
        IReadOnlyCollection<ExtraServiceLine> extraServices, IReadOnlyDictionary<string, int> equipmentStock)
    {
        var duplicate = extraServices.GroupBy(e => e.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                $"El servicio '{duplicate.Key}' está repetido; indica una sola línea con la cantidad total.");
        }

        foreach (var line in extraServices.Where(e => e.Type == ExtraServiceType.OwnEquipment))
        {
            var stock = equipmentStock.TryGetValue(line.Code, out var s) ? s : 0;
            if (line.Quantity > stock)
            {
                throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                    $"No hay suficiente '{line.Name}': se pidieron {line.Quantity} y el inventario es {stock}.");
            }
        }
    }
}
