using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

/// <summary>
/// Domain service for BR-21 / INV-05: cancelling never refunds money; everything accredited
/// becomes customer credit in full.
/// </summary>
public static class CancellationPolicy
{
    public static Money CreditFor(GuaranteeDeposit? deposit) => deposit?.Accredited ?? Money.Zero;
}
