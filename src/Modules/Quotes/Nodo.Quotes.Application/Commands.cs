using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Domain;
using Nodo.SharedKernel;

namespace Nodo.Quotes.Application;

// ---- PreviewQuote (query: computes, persists nothing) ----

public sealed record PreviewQuoteQuery(
    Guid CustomerId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End, int Attendance,
    IReadOnlyCollection<ExtraServiceRequest> ExtraServices) : IQuery<QuotePreviewDto>;

internal sealed class PreviewQuoteValidator : AbstractValidator<PreviewQuoteQuery>
{
    public PreviewQuoteValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("El cliente es obligatorio.");
        RuleFor(x => x.HallId).NotEmpty().WithMessage("El salón es obligatorio.");
        RuleFor(x => x.ExtraServices).NotNull().WithMessage("La lista de servicios extra es obligatoria (puede estar vacía).");
        RuleForEach(x => x.ExtraServices).ChildRules(e =>
            e.RuleFor(s => s.Code).NotEmpty().WithMessage("Cada servicio extra requiere su código."));
    }
}

internal sealed class PreviewQuoteHandler(QuoteDrafter drafter) : IQueryHandler<PreviewQuoteQuery, QuotePreviewDto>
{
    public async Task<QuotePreviewDto> Handle(PreviewQuoteQuery request, CancellationToken cancellationToken)
    {
        var quote = await drafter.BuildAsync(
            request.CustomerId, request.HallId, request.Date, request.Start, request.End, request.Attendance,
            request.ExtraServices, cancellationToken);

        return new QuotePreviewDto(
            quote.Price.ToDto(),
            quote.ExtraServices.Select(e => e.ToDto()).ToList(),
            await drafter.CheckAvailabilityAsync(quote, cancellationToken));
    }
}

// ---- IssueQuote ----

public sealed record IssueQuoteCommand(
    string PerformedBy, Guid CustomerId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End, int Attendance,
    IReadOnlyCollection<ExtraServiceRequest> ExtraServices) : ICommand<IssuedQuoteDto>;

internal sealed class IssueQuoteValidator : AbstractValidator<IssueQuoteCommand>
{
    public IssueQuoteValidator()
    {
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién emite la cotización.");
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("El cliente es obligatorio.");
        RuleFor(x => x.HallId).NotEmpty().WithMessage("El salón es obligatorio.");
        RuleFor(x => x.ExtraServices).NotNull().WithMessage("La lista de servicios extra es obligatoria (puede estar vacía).");
        RuleForEach(x => x.ExtraServices).ChildRules(e =>
            e.RuleFor(s => s.Code).NotEmpty().WithMessage("Cada servicio extra requiere su código."));
    }
}

internal sealed class IssueQuoteHandler(
    QuoteDrafter drafter, IQuoteRepository quotes, IQuotesUnitOfWork unitOfWork)
    : ICommandHandler<IssueQuoteCommand, IssuedQuoteDto>
{
    public async Task<IssuedQuoteDto> Handle(IssueQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await drafter.BuildAsync(
            request.CustomerId, request.HallId, request.Date, request.Start, request.End, request.Attendance,
            request.ExtraServices, cancellationToken);
        var availability = await drafter.CheckAvailabilityAsync(quote, cancellationToken); // informative only

        await quotes.AddAsync(quote, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedQuoteDto(
            quote.Id.Value, quote.IssuedAtUtc, quote.ExpiresAtUtc, quote.Price.ToDto(),
            quote.ExtraServices.Select(e => e.ToDto()).ToList(), availability);
    }
}

// ---- AcceptQuote ----

public sealed record AcceptQuoteCommand(Guid QuoteId, string PerformedBy) : ICommand<AcceptedQuoteDto>;

internal sealed class AcceptQuoteValidator : AbstractValidator<AcceptQuoteCommand>
{
    public AcceptQuoteValidator()
    {
        RuleFor(x => x.QuoteId).NotEmpty().WithMessage("La cotización es obligatoria.");
        RuleFor(x => x.PerformedBy).NotEmpty().WithMessage("Indica quién acepta la cotización.");
    }
}

internal sealed class AcceptQuoteHandler(
    IQuoteRepository quotes, IQuotesUnitOfWork unitOfWork, IHallScheduleModuleApi hallSchedule, IClock clock)
    : ICommandHandler<AcceptQuoteCommand, AcceptedQuoteDto>
{
    public async Task<AcceptedQuoteDto> Handle(AcceptQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await quotes.GetByIdAsync(new QuoteId(request.QuoteId), cancellationToken)
            ?? throw new NotFoundException($"No existe la cotización '{request.QuoteId}'.");

        // The quote validates itself first (expiry, state, booking window). Nothing is saved until availability holds.
        quote.Accept(clock.UtcNow, clock.TodayLocal);

        // BR-10: issuing only informed availability; accepting requires it.
        var equipment = quote.ExtraServices
            .Where(e => e.Type == ExtraServiceType.OwnEquipment)
            .Select(e => new EquipmentRequest(e.Code, e.Quantity))
            .ToList();
        var availability = await hallSchedule.CheckAvailabilityAsync(
            quote.HallId.Value, quote.Slot.Date, quote.Slot.Start, quote.Slot.End, equipment, null, cancellationToken);
        if (!availability.Success)
            throw new DomainException(availability.Code!, availability.Reason!);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AcceptedQuoteDto(quote.Id.Value, quote.Status.ToString(), quote.AcceptedAtUtc!.Value);
    }
}

// ---- ExpireDueQuotes (job, spec 04) ----

public sealed record ExpireDueQuotesCommand : ICommand<int>;

internal sealed class ExpireDueQuotesHandler(IQuoteRepository quotes, IQuotesUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ExpireDueQuotesCommand, int>
{
    public async Task<int> Handle(ExpireDueQuotesCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await quotes.ListDueForExpirationAsync(now, cancellationToken);
        var expired = due.Count(quote => quote.Expire(now));

        if (expired > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return expired;
    }
}
