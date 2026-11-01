using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Application;

// Domain event -> integration event. Explicit mapping: the integration event is the public contract.

internal sealed class EventRequestCreatedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<EventRequestCreated>
{
    protected override Task HandleAsync(EventRequestCreated e, CancellationToken cancellationToken)
    {
        outbox.Add(new EventRequestCreatedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.Start, e.End,
            e.Attendance, e.CustomerId.Value));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventApprovedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventApprovedByManagement>
{
    protected override Task HandleAsync(HallEventApprovedByManagement e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventApprovedByManagementIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.ApprovedBy, e.DepositPercentage,
            e.DepositRequired.Amount, e.DepositDeadlineUtc));
        return Task.CompletedTask;
    }
}

internal sealed class EventRequestRejectedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<EventRequestRejected>
{
    protected override Task HandleAsync(EventRequestRejected e, CancellationToken cancellationToken)
    {
        outbox.Add(new EventRequestRejectedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.RejectedBy, e.Reason));
        return Task.CompletedTask;
    }
}

internal sealed class GuaranteeDepositAccreditedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<GuaranteeDepositAccredited>
{
    protected override Task HandleAsync(GuaranteeDepositAccredited e, CancellationToken cancellationToken)
    {
        outbox.Add(new GuaranteeDepositAccreditedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.Amount.Amount, e.TransactionId,
            e.AccreditedTotal.Amount, e.IsCovered));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventFirmlyConfirmedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventFirmlyConfirmed>
{
    protected override Task HandleAsync(HallEventFirmlyConfirmed e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventFirmlyConfirmedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.Start, e.End, e.Attendance,
            e.CustomerId.Value, e.Total.Amount, e.DepositAccredited.Amount,
            e.ExtraServices.Select(x => x.ToContract()).ToList()));
        return Task.CompletedTask;
    }
}

internal sealed class ExtraServicesAssignedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<ExtraServicesAssigned>
{
    protected override Task HandleAsync(ExtraServicesAssigned e, CancellationToken cancellationToken)
    {
        outbox.Add(new ExtraServicesAssignedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.ExtraServices.Select(x => x.ToContract()).ToList()));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventStartedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventStarted>
{
    protected override Task HandleAsync(HallEventStarted e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventStartedIntegrationEvent(Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.StartedBy));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventCompletedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventCompleted>
{
    protected override Task HandleAsync(HallEventCompleted e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventCompletedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.CustomerId.Value, e.Total.Amount,
            e.DepositAccredited.Amount, e.RemainingToSettle.Amount));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventCancelledMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventCancelledWithCredit>
{
    protected override Task HandleAsync(HallEventCancelledWithCredit e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventCancelledWithCreditIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.CustomerId.Value,
            e.CreditAmount.Amount));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventRescheduledMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventRescheduledWithCredit>
{
    protected override Task HandleAsync(HallEventRescheduledWithCredit e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventRescheduledWithCreditIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.CustomerId.Value, e.NewDate,
            e.NewStart, e.NewEnd, e.CreditApplied.Amount, e.ResultingStatus.ToString(), e.DepositPending.Amount,
            e.DepositDeadlineUtc));
        return Task.CompletedTask;
    }
}

internal sealed class HallEventExpiredMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<HallEventExpired>
{
    protected override Task HandleAsync(HallEventExpired e, CancellationToken cancellationToken)
    {
        outbox.Add(new HallEventExpiredIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.CustomerId.Value,
            e.CreditAmount.Amount));
        return Task.CompletedTask;
    }
}

internal sealed class CustomerCreditGrantedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<CustomerCreditGranted>
{
    protected override Task HandleAsync(CustomerCreditGranted e, CancellationToken cancellationToken)
    {
        outbox.Add(new CustomerCreditGrantedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.CustomerId.Value, e.EventId.Value, e.Amount.Amount, e.Balance.Amount));
        return Task.CompletedTask;
    }
}

internal sealed class CustomerCreditAppliedMapper(IEventLifecycleOutbox outbox) : DomainEventHandler<CustomerCreditApplied>
{
    protected override Task HandleAsync(CustomerCreditApplied e, CancellationToken cancellationToken)
    {
        outbox.Add(new CustomerCreditAppliedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.CustomerId.Value, e.EventId.Value, e.Amount.Amount, e.Balance.Amount));
        return Task.CompletedTask;
    }
}
