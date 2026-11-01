using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using Nodo.EventLifecycle.Application;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.External.Simulated.Spaces;

namespace Nodo.EventLifecycle.Infrastructure;

/// <summary>Customer/Supplier with Open Host Service: EventLifecycle only needs the stock of each own-equipment item.</summary>
internal sealed class SpacesCatalogAdapter(SpacesContext spaces) : ISpacesCatalog
{
    public Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(spaces.Venues
            .SelectMany(v => v.Assets)
            .ToDictionary(a => a.AssetCode, a => a.UnitsInStock, StringComparer.OrdinalIgnoreCase));
}

internal sealed class CustomerDirectoryAdapter(MembershipsContext memberships) : ICustomerDirectory
{
    public Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(memberships.Find(customerId.ToString())?.FullName);
}

/// <summary>What the webhook answers to the payment provider.</summary>
public sealed record WebhookAck(
    bool Accredited, string Outcome, Guid? EventId, string? EventStatus, decimal? AccreditedTotal, decimal? ExcessGrantedAsCredit);

/// <summary>Anti-corruption layer between the payment provider and EventLifecycle.</summary>
public interface IPaymentGatewayAcl
{
    /// <exception cref="ValidationException">The payload is malformed (HTTP 400).</exception>
    Task<WebhookAck> ReceiveAsync(GatewayWebhookPayload payload, CancellationToken cancellationToken = default);
}

/// <summary>
/// Translates the provider's "ugly" payload into our language: <c>amount_cents</c> into bolivianos, <c>merchant_ref</c>
/// into the event id, and an approved payment into <see cref="AccreditDepositCommand"/>. A declined payment is
/// acknowledged and logged but never reaches the domain. The same <c>txn_id</c> arriving twice is harmless: the
/// aggregate accredits each transaction once.
/// </summary>
internal sealed class PaymentGatewayAclAdapter(IMediator mediator, ILogger<PaymentGatewayAclAdapter> logger) : IPaymentGatewayAcl
{
    public const string Approved = "APPROVED";
    public const string Declined = "DECLINED";

    public async Task<WebhookAck> ReceiveAsync(GatewayWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        var failures = Validate(payload, out var eventId);
        if (failures.Count > 0)
            throw new ValidationException(failures);

        if (payload.Status!.Equals(Declined, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Payment {TxnId} for event {EventId} was declined by the provider ({Cents} cents); nothing accredited",
                payload.TxnId, eventId, payload.AmountCents);
            return new WebhookAck(false, "DECLINED_NOT_ACCREDITED", eventId, null, null, null);
        }

        var result = await mediator.Send(
            new AccreditDepositCommand(eventId, payload.AmountCents / 100m, payload.CurrencyCode!, payload.TxnId!),
            cancellationToken);

        return new WebhookAck(
            true, result.IsCovered ? "ACCREDITED_AND_COVERED" : "ACCREDITED", eventId, result.Status,
            result.AccreditedTotal, result.ExcessGrantedAsCredit);
    }

    private static List<ValidationFailure> Validate(GatewayWebhookPayload payload, out Guid eventId)
    {
        var failures = new List<ValidationFailure>();
        eventId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(payload.TxnId))
            failures.Add(new ValidationFailure("txn_id", "txn_id es obligatorio."));

        if (string.IsNullOrWhiteSpace(payload.CurrencyCode))
            failures.Add(new ValidationFailure("currency_code", "currency_code es obligatorio."));

        if (!string.Equals(payload.Status, Approved, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payload.Status, Declined, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new ValidationFailure("status", "status debe ser APPROVED o DECLINED."));
        }

        if (!Guid.TryParse(payload.Metadata?.MerchantRef, out eventId) || eventId == Guid.Empty)
            failures.Add(new ValidationFailure("metadata.merchant_ref", "merchant_ref debe ser el identificador de un evento."));

        return failures;
    }
}
