using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Domain;

/// <summary>Occupation of the hall for one day (one aggregate per hall and date).</summary>
public sealed class HallSchedule : AggregateRoot<HallScheduleId>
{
    private readonly List<ScheduleBlock> _blocks = [];

    // The composite identity (hall, day) is stored as two columns; EF rebuilds the aggregate through this constructor.
    private HallSchedule(HallId hallId, DateOnly date) : base(new HallScheduleId(hallId, date))
    {
        HallId = hallId;
        Date = date;
    }

    public HallId HallId { get; }

    public DateOnly Date { get; }

    public IReadOnlyCollection<ScheduleBlock> Blocks => _blocks.AsReadOnly();

    public IEnumerable<ScheduleBlock> ActiveBlocks => _blocks.Where(b => b.IsActive);

    public static HallSchedule Create(HallId hallId, DateOnly date) => new(hallId, date);

    public void PlaceProvisionalBlock(
        EventId eventId, TimeSlot slot, IReadOnlyCollection<EquipmentAllocation> equipment,
        IReadOnlyDictionary<string, int> inventory, DateTime nowUtc)
    {
        Place(eventId, slot, equipment, inventory, BlockKind.Provisional, nowUtc);
        RaiseDomainEvent(new SlotProvisionallyBlocked(eventId, HallId, Date, slot.Start, slot.End, nowUtc));
    }

    /// <summary>Used when a rescheduled event is already covered by credit.</summary>
    public void PlaceDefinitiveBlock(
        EventId eventId, TimeSlot slot, IReadOnlyCollection<EquipmentAllocation> equipment,
        IReadOnlyDictionary<string, int> inventory, DateTime nowUtc)
    {
        Place(eventId, slot, equipment, inventory, BlockKind.Definitive, nowUtc);
        RaiseDomainEvent(new SlotBlockConsolidated(eventId, HallId, Date, slot.Start, slot.End, nowUtc));
    }

    /// <summary>Provisional to Definitive. Idempotent; a no-op when the event has no active block.</summary>
    public void Consolidate(EventId eventId, DateTime nowUtc)
    {
        var block = ActiveBlockOf(eventId);
        if (block is null || !block.Consolidate())
            return;

        RaiseDomainEvent(new SlotBlockConsolidated(eventId, HallId, Date, block.Slot.Start, block.Slot.End, nowUtc));
    }

    /// <summary>Frees the event's slot. Idempotent.</summary>
    public void Release(EventId eventId, DateTime nowUtc)
    {
        var block = ActiveBlockOf(eventId);
        if (block is null || !block.Release(nowUtc))
            return;

        RaiseDomainEvent(new SlotReleased(eventId, HallId, Date, block.Slot.Start, block.Slot.End, nowUtc));
    }

    private void Place(
        EventId eventId, TimeSlot slot, IReadOnlyCollection<EquipmentAllocation> equipment,
        IReadOnlyDictionary<string, int> inventory, BlockKind kind, DateTime nowUtc)
    {
        // Validate everything before mutating.
        if (ActiveBlockOf(eventId) is not null) // INV-19
        {
            throw new DomainException(InvariantCodes.DuplicateBlock,
                "El evento ya tiene un bloqueo activo en el salón.");
        }

        var availability = AvailabilityChecker.Check(this, slot, equipment, inventory);
        if (!availability.IsAvailable)
            throw new DomainException(availability.Code!, availability.Reason!);

        _blocks.Add(ScheduleBlock.Create(eventId, slot, kind, equipment, nowUtc));
    }

    private ScheduleBlock? ActiveBlockOf(EventId eventId) =>
        _blocks.FirstOrDefault(b => b.IsActive && b.EventId == eventId);
}
