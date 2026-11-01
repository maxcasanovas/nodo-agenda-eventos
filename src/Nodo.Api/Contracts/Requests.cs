using Nodo.Quotes.Application;

namespace Nodo.Api.Contracts;

// Request bodies of the REST API. They are shaped for the caller; the controllers translate them into use-case commands.
// Shape rules (required, format) are checked by the commands' validators; business rules live in the domain.

/// <summary>Who performs an action when the caller does not say (the API has no authentication, BR-24).</summary>
public static class Staff
{
    public const string Management = "Lucía Paz";
    public const string Reception = "Recepción Nodo";
    public const string Operations = "Host de turno";
    public const string Customer = "Cliente";
}

/// <summary>A quote request. <see cref="HallId"/> defaults to the only hall; extras are catalog codes.</summary>
public sealed record PreviewQuoteRequest(
    Guid CustomerId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int Attendance,
    IReadOnlyCollection<ExtraServiceRequest>? ExtraServices = null, Guid? HallId = null);

public sealed record IssueQuoteRequest(
    Guid CustomerId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, int Attendance,
    IReadOnlyCollection<ExtraServiceRequest>? ExtraServices = null, Guid? HallId = null, string? PerformedBy = null);

public sealed record AcceptQuoteRequest(string? AcceptedBy = null);

public sealed record ApproveRequest(int DepositPercentage, string ApprovedBy);

public sealed record RejectRequest(string Reason, string RejectedBy);

public sealed record PerformedByRequest(string? PerformedBy = null);

public sealed record CancelRequest(string? Reason, string CancelledBy);

/// <summary>The new slot keeps the original duration: only the day and the start time are chosen (BR-23).</summary>
public sealed record RescheduleRequest(DateOnly NewDate, TimeOnly NewStartTime, string PerformedBy);

/// <summary>Pays a payment link as the provider would. Without <see cref="Amount"/> it pays what is still owed.</summary>
public sealed record PayLinkRequest(decimal? Amount = null, bool Decline = false);
