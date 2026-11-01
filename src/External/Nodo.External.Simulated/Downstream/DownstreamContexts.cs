namespace Nodo.External.Simulated.Downstream;

/// <summary>Thread-safe append-only list shared by the simulated downstream systems.</summary>
public sealed class InMemoryLog<T>
{
    private readonly object _gate = new();
    private readonly List<T> _items = [];

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
                return _items.ToList();
        }
    }

    public void Add(T item)
    {
        lock (_gate)
            _items.Add(item);
    }

    public void Update(Func<T, bool> match, Func<T, T> change)
    {
        lock (_gate)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (match(_items[i]))
                    _items[i] = change(_items[i]);
            }
        }
    }
}

// ---- Facturación y Cobros ----

public sealed record LedgerEntry(
    Guid EntryId, DateTime AtUtc, string Kind, string Account, string Reference, decimal Amount, string Currency, string Note);

public sealed class BillingContext
{
    public InMemoryLog<LedgerEntry> Ledger { get; } = new();
}

// ---- Control de Acceso ----

/// <summary>A temporary credential for an event: a 6-digit PIN and the text of its QR code. Status: ACTIVE or REVOKED.</summary>
public sealed record AccessCredential(
    Guid CredentialId, string EventRef, string Pin, string QrText, int Capacity, DateTime ValidFromUtc,
    DateTime ValidUntilUtc, string Status, DateTime? RevokedAtUtc);

public sealed class AccessControlContext
{
    public InMemoryLog<AccessCredential> Credentials { get; } = new();
}

// ---- Notificaciones ----

public sealed record Notification(Guid Id, string To, string Channel, string Subject, string Body, DateTime SentAt);

public sealed class NotificationsContext
{
    public InMemoryLog<Notification> Feed { get; } = new();
}

// ---- Proveedor de Catering ----

public sealed record CateringOrderLine(string Sku, string Title, int Headcount, decimal PricePerHead);

public sealed record CateringOrder(
    Guid OrderId, string EventRef, DateTime ReceivedAtUtc, string Status, IReadOnlyList<CateringOrderLine> Lines);

public sealed class CateringContext
{
    public InMemoryLog<CateringOrder> Orders { get; } = new();
}

/// <summary>Event id -> customer name, learned from the events themselves so later notifications can name the customer.</summary>
public sealed class EventDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string> _customerByEvent = [];

    public void Remember(Guid eventId, string customerName)
    {
        lock (_gate)
            _customerByEvent[eventId] = customerName;
    }

    public string? CustomerOf(Guid eventId)
    {
        lock (_gate)
            return _customerByEvent.GetValueOrDefault(eventId);
    }
}

/// <summary>
/// The simulated systems are plain singletons that do not share our transaction or inbox, so each handler remembers
/// the messages it already processed: at-least-once delivery must not duplicate a credential or a notification.
/// </summary>
public sealed class ProcessedMessages
{
    private readonly object _gate = new();
    private readonly HashSet<(Guid, string)> _seen = [];

    /// <returns>False when this consumer already handled the message.</returns>
    public bool TryMark(Guid messageId, string consumer)
    {
        lock (_gate)
            return _seen.Add((messageId, consumer));
    }
}
