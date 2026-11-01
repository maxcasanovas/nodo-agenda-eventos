using System.Text.Json.Serialization;

namespace Nodo.External.Simulated.Payments;

/// <summary>The provider's webhook payload, exactly as it sends it (snake_case, amounts in cents). Our ACL translates it.</summary>
public sealed class GatewayWebhookPayload
{
    [JsonPropertyName("txn_id")]
    public string? TxnId { get; set; }

    [JsonPropertyName("amount_cents")]
    public long AmountCents { get; set; }

    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    /// <summary>"APPROVED" or "DECLINED".</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("metadata")]
    public GatewayMetadata? Metadata { get; set; }
}

public sealed class GatewayMetadata
{
    /// <summary>The reference we gave when the link was created: our event id.</summary>
    [JsonPropertyName("merchant_ref")]
    public string? MerchantRef { get; set; }
}

public sealed record PaymentAttempt(string TxnId, long AmountCents, string Status, DateTime AtUtc);

/// <summary>A payment link of the gateway. Status: PENDING, PARTIALLY_PAID, PAID or DECLINED.</summary>
public sealed class PaymentLink
{
    private readonly List<PaymentAttempt> _attempts = [];

    public required string LinkId { get; init; }

    public required string MerchantRef { get; init; }

    public required long AmountCents { get; init; }

    public required string CurrencyCode { get; init; }

    public required string Description { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public string Status { get; internal set; } = "PENDING";

    public long PaidCents { get; internal set; }

    public IReadOnlyList<PaymentAttempt> Attempts => _attempts;

    internal void AddAttempt(PaymentAttempt attempt) => _attempts.Add(attempt);
}

public sealed class PaymentGatewayContext
{
    private readonly object _gate = new();
    private readonly List<PaymentLink> _links = [];

    public IReadOnlyList<PaymentLink> Links
    {
        get
        {
            lock (_gate)
                return _links.OrderByDescending(l => l.CreatedAtUtc).ToList();
        }
    }

    public PaymentLink CreateLink(string merchantRef, long amountCents, string description, DateTime nowUtc)
    {
        var link = new PaymentLink
        {
            LinkId = $"LNK-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            MerchantRef = merchantRef,
            AmountCents = amountCents,
            CurrencyCode = "BOB",
            Description = description,
            CreatedAtUtc = nowUtc
        };

        lock (_gate)
            _links.Add(link);
        return link;
    }

    public PaymentLink? Find(string linkId)
    {
        lock (_gate)
            return _links.FirstOrDefault(l => string.Equals(l.LinkId, linkId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Records what the provider just sent for a link and updates its status. Declined payments are kept, not lost.</summary>
    public void RecordAttempt(PaymentLink link, string txnId, long amountCents, bool approved, DateTime nowUtc)
    {
        lock (_gate)
        {
            link.AddAttempt(new PaymentAttempt(txnId, amountCents, approved ? "APPROVED" : "DECLINED", nowUtc));
            if (!approved)
            {
                if (link.PaidCents == 0)
                    link.Status = "DECLINED";
                return;
            }

            link.PaidCents += amountCents;
            link.Status = link.PaidCents >= link.AmountCents ? "PAID" : "PARTIALLY_PAID";
        }
    }
}
