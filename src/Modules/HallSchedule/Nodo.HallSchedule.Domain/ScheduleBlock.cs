using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Domain;

/// <summary>A reservation of the hall. Only the <see cref="HallSchedule"/> root creates or changes blocks.</summary>
public sealed class ScheduleBlock : Entity<ScheduleBlockId>
{
    private readonly List<EquipmentAllocation> _equipment = [];

    private ScheduleBlock() : base(default)
    {
        Slot = null!;
    }

    private ScheduleBlock(
        ScheduleBlockId id, EventId eventId, TimeSlot slot, BlockKind kind,
        IEnumerable<EquipmentAllocation> equipment, DateTime nowUtc) : base(id)
    {
        EventId = eventId;
        Slot = slot;
        Kind = kind;
        Status = BlockStatus.Active;
        CreatedAtUtc = nowUtc;
        _equipment.AddRange(equipment);
    }

    public EventId EventId { get; private set; }

    public TimeSlot Slot { get; private set; }

    public BlockKind Kind { get; private set; }

    public BlockStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }

    public IReadOnlyCollection<EquipmentAllocation> EquipmentAllocations => _equipment.AsReadOnly();

    public bool IsActive => Status == BlockStatus.Active;

    internal static ScheduleBlock Create(
        EventId eventId, TimeSlot slot, BlockKind kind, IEnumerable<EquipmentAllocation> equipment, DateTime nowUtc) =>
        new(ScheduleBlockId.New(), eventId, slot, kind, equipment, nowUtc);

    /// <returns>True when the block changed (idempotent otherwise).</returns>
    internal bool Consolidate()
    {
        if (!IsActive || Kind == BlockKind.Definitive)
            return false;

        Kind = BlockKind.Definitive;
        return true;
    }

    /// <returns>True when the block changed (idempotent otherwise).</returns>
    internal bool Release(DateTime nowUtc)
    {
        if (!IsActive)
            return false;

        Status = BlockStatus.Released;
        ReleasedAtUtc = nowUtc;
        return true;
    }
}
