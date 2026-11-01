using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;
using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Application;

// Handlers only orchestrate: load, call the aggregate, move credit between aggregates of this module, save.
// Every business rule lives in HallEvent, GuaranteeDeposit and CustomerCredit.

// ---- ApproveEventRequest ----

public sealed record ApproveEventRequestCommand(Guid EventId, int DepositPercentage, string ApprovedBy)
    : ICommand<HallEventStatusDto>;

internal sealed class ApproveEventRequestValidator : AbstractValidator<ApproveEventRequestCommand>
{
    public ApproveEventRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.ApprovedBy).NotEmpty().WithMessage("Indica quién aprueba.");
    }
}

internal sealed class ApproveEventRequestHandler(IHallEventRepository events, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ApproveEventRequestCommand, HallEventStatusDto>
{
    public async Task<HallEventStatusDto> Handle(ApproveEventRequestCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);

        hallEvent.Approve(request.ApprovedBy, request.DepositPercentage, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HallEventStatusDto(hallEvent.Id.Value, hallEvent.Status.ToString());
    }
}

// ---- RejectEventRequest ----

public sealed record RejectEventRequestCommand(Guid EventId, string Reason, string RejectedBy)
    : ICommand<HallEventStatusDto>;

internal sealed class RejectEventRequestValidator : AbstractValidator<RejectEventRequestCommand>
{
    public RejectEventRequestValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.RejectedBy).NotEmpty().WithMessage("Indica quién rechaza.");
    }
}

internal sealed class RejectEventRequestHandler(IHallEventRepository events, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RejectEventRequestCommand, HallEventStatusDto>
{
    public async Task<HallEventStatusDto> Handle(RejectEventRequestCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);

        hallEvent.Reject(request.RejectedBy, request.Reason, clock.UtcNow); // INV-08 validates the reason length

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HallEventStatusDto(hallEvent.Id.Value, hallEvent.Status.ToString());
    }
}

// ---- AccreditDeposit (invoked by the payment gateway ACL) ----

public sealed record AccreditDepositCommand(Guid EventId, decimal Amount, string Currency, string TransactionId)
    : ICommand<AccreditDepositResultDto>;

internal sealed class AccreditDepositValidator : AbstractValidator<AccreditDepositCommand>
{
    public AccreditDepositValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.TransactionId).NotEmpty().WithMessage("El identificador de la transacción es obligatorio.");
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithMessage("La moneda debe ser un código de 3 letras.");
    }
}

internal sealed class AccreditDepositHandler(
    IHallEventRepository events, CustomerCreditAccessor credits, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<AccreditDepositCommand, AccreditDepositResultDto>
{
    public async Task<AccreditDepositResultDto> Handle(AccreditDepositCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);
        var now = clock.UtcNow;

        // Confirms in firm by itself when the deposit becomes covered (BR-16) and returns what exceeded the deposit.
        var excess = hallEvent.AccreditDeposit(Money.Of(request.Amount, request.Currency), request.TransactionId, now);

        if (excess.Amount > 0) // BR-15: the excess becomes customer credit, in the same transaction
        {
            var credit = await credits.GetOrOpenAsync(hallEvent.CustomerId, cancellationToken);
            credit.Grant(excess, hallEvent.Id, "Excedente del depósito de garantía", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var deposit = hallEvent.Deposit!;
        return new AccreditDepositResultDto(
            hallEvent.Id.Value, hallEvent.Status.ToString(), deposit.Accredited.Amount, deposit.Pending.Amount,
            deposit.IsCovered, excess.Amount);
    }
}

// ---- StartHallEvent / CompleteHallEvent ----

public sealed record StartHallEventCommand(Guid EventId, string PerformedBy) : ICommand<HallEventStatusDto>;

public sealed record CompleteHallEventCommand(Guid EventId, string PerformedBy) : ICommand<HallEventStatusDto>;

internal sealed class StartHallEventValidator : AbstractValidator<StartHallEventCommand>
{
    public StartHallEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién inicia el evento.");
    }
}

internal sealed class CompleteHallEventValidator : AbstractValidator<CompleteHallEventCommand>
{
    public CompleteHallEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién cierra el evento.");
    }
}

internal sealed class StartHallEventHandler(IHallEventRepository events, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<StartHallEventCommand, HallEventStatusDto>
{
    public async Task<HallEventStatusDto> Handle(StartHallEventCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);

        hallEvent.Start(clock.UtcNow, request.PerformedBy);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HallEventStatusDto(hallEvent.Id.Value, hallEvent.Status.ToString());
    }
}

internal sealed class CompleteHallEventHandler(IHallEventRepository events, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CompleteHallEventCommand, HallEventStatusDto>
{
    public async Task<HallEventStatusDto> Handle(CompleteHallEventCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);

        hallEvent.Complete(clock.UtcNow, request.PerformedBy);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HallEventStatusDto(hallEvent.Id.Value, hallEvent.Status.ToString());
    }
}

// ---- CancelHallEvent ----

public sealed record CancelHallEventCommand(Guid EventId, string? Reason, string PerformedBy)
    : ICommand<HallEventStatusDto>;

internal sealed class CancelHallEventValidator : AbstractValidator<CancelHallEventCommand>
{
    public CancelHallEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién cancela el evento.");
    }
}

internal sealed class CancelHallEventHandler(
    IHallEventRepository events, CustomerCreditAccessor credits, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CancelHallEventCommand, HallEventStatusDto>
{
    public async Task<HallEventStatusDto> Handle(CancelHallEventCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);
        var now = clock.UtcNow;

        hallEvent.Cancel(request.PerformedBy, request.Reason, now);

        if (hallEvent.CreditGenerated.Amount > 0) // BR-21: no refund, everything accredited becomes credit
        {
            var credit = await credits.GetOrOpenAsync(hallEvent.CustomerId, cancellationToken);
            credit.Grant(hallEvent.CreditGenerated, hallEvent.Id, "Cancelación del evento", now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new HallEventStatusDto(hallEvent.Id.Value, hallEvent.Status.ToString());
    }
}

// ---- RescheduleHallEvent ----

public sealed record RescheduleHallEventCommand(
    Guid EventId, DateOnly NewDate, TimeOnly NewStart, string PerformedBy)
    : ICommand<RescheduleResultDto>;

internal sealed class RescheduleHallEventValidator : AbstractValidator<RescheduleHallEventCommand>
{
    public RescheduleHallEventValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("El evento es obligatorio.");
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién reprograma el evento.");
    }
}

internal sealed class RescheduleHallEventHandler(
    IHallEventRepository events,
    CustomerCreditAccessor credits,
    IHallScheduleModuleApi hallSchedule,
    IEventLifecycleUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<RescheduleHallEventCommand, RescheduleResultDto>
{
    public async Task<RescheduleResultDto> Handle(RescheduleHallEventCommand request, CancellationToken cancellationToken)
    {
        var hallEvent = await events.GetRequiredAsync(request.EventId, cancellationToken);
        var now = clock.UtcNow;
        var today = clock.TodayLocal;
        var newSlot = hallEvent.SlotMovedTo(request.NewDate, request.NewStart); // same duration (BR-23)
        var equipment = HallEventMapping.OwnEquipmentOf(hallEvent);

        // 1. Fail early, touching nothing: the event must be reschedulable and the new slot free.
        hallEvent.EnsureCanReschedule(newSlot, today);
        var availability = await hallSchedule.CheckAvailabilityAsync(
            hallEvent.HallId.Value, newSlot.Date, newSlot.Start, newSlot.End, equipment,
            hallEvent.Id.Value, cancellationToken);
        if (!availability.Success)
            throw new DomainException(availability.Code!, availability.Reason!);

        // 2. Use the customer's credit up to the required deposit, then let the event reschedule itself (BR-23).
        var credit = await credits.FindAsync(hallEvent.CustomerId, cancellationToken)
            ?? CustomerCredit.Open(hallEvent.CustomerId); // not persisted: Apply fails with CREDIT.INSUFFICIENT
        var applied = credit.Apply(hallEvent.Deposit!.Required, hallEvent.Id, now);
        hallEvent.Reschedule(newSlot, applied, request.PerformedBy, now, today);

        // 3. Reserve the slot: definitive when the credit covered the deposit, provisional otherwise.
        var placement = hallEvent.Status == HallEventStatus.Confirmed
            ? await hallSchedule.PlaceDefinitiveBlockAsync(
                hallEvent.Id.Value, hallEvent.HallId.Value, newSlot.Date, newSlot.Start, newSlot.End,
                equipment, cancellationToken)
            : await hallSchedule.PlaceProvisionalBlockAsync(
                hallEvent.Id.Value, hallEvent.HallId.Value, newSlot.Date, newSlot.Start, newSlot.End,
                equipment, cancellationToken);
        if (!placement.Success)
            throw new DomainException(placement.Code!, placement.Reason!); // nothing was saved: the event is untouched

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RescheduleResultDto(
            hallEvent.Id.Value, hallEvent.Status.ToString(), applied.Amount, hallEvent.Deposit!.Pending.Amount,
            hallEvent.DepositDeadlineUtc);
    }
}

// ---- ExpireOverdueHallEvents (job, spec 04) ----

public sealed record ExpireOverdueHallEventsCommand : ICommand<int>;

internal sealed class ExpireOverdueHallEventsHandler(
    IHallEventRepository events, CustomerCreditAccessor credits, IEventLifecycleUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ExpireOverdueHallEventsCommand, int>
{
    public async Task<int> Handle(ExpireOverdueHallEventsCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var overdue = await events.ListOverdueAsync(now, cancellationToken);

        foreach (var hallEvent in overdue)
        {
            hallEvent.Expire(now);

            if (hallEvent.CreditGenerated.Amount > 0) // BR-17: a partially paid deposit becomes credit
            {
                var credit = await credits.GetOrOpenAsync(hallEvent.CustomerId, cancellationToken);
                credit.Grant(hallEvent.CreditGenerated, hallEvent.Id, "Depósito parcial de un evento vencido", now);
            }
        }

        if (overdue.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return overdue.Count;
    }
}
