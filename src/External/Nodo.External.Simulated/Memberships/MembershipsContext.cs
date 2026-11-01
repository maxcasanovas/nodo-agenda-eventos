namespace Nodo.External.Simulated.Memberships;

/// <summary>
/// "Membresías y Planes" record. <see cref="Status"/> is "ACTIVE", "EXPIRED" or "NONE" (external customer without
/// plan); <see cref="DiscountPct"/> is what the provider reports. Our module adopts both as published (Conformist).
/// </summary>
public sealed record MemberRecord(
    string MemberId, string FullName, string Kind, string? Plan, string Status, int DiscountPct);

public sealed class MembershipsContext
{
    private readonly object _gate = new();
    private readonly List<MemberRecord> _members =
    [
        new("22222222-0000-0000-0000-000000000001", "Ana Gutiérrez", "Freelancer", "Flexible", "ACTIVE", 5),
        new("22222222-0000-0000-0000-000000000002", "TechSol S.R.L.", "Empresa", "Equipo", "ACTIVE", 10),
        new("22222222-0000-0000-0000-000000000003", "Carlos Méndez", "Freelancer", "Dedicado", "EXPIRED", 0),
        new("22222222-0000-0000-0000-000000000004", "Fundación Crecer", "Externo", null, "NONE", 0)
    ];

    public IReadOnlyList<MemberRecord> Members
    {
        get
        {
            lock (_gate)
                return _members.ToList();
        }
    }

    public MemberRecord? Find(string memberId)
    {
        lock (_gate)
            return _members.FirstOrDefault(m => string.Equals(m.MemberId, memberId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a record until the returned handle is disposed. Used by the INV-17 probe to simulate an upstream that
    /// reports a discount the domain must refuse; the record never outlives the probe.
    /// </summary>
    public IDisposable AddTemporarily(MemberRecord record)
    {
        lock (_gate)
            _members.Add(record);

        return new Removal(this, record);
    }

    private sealed class Removal(MembershipsContext owner, MemberRecord record) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
                owner._members.Remove(record);
        }
    }
}
