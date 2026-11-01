using Nodo.BuildingBlocks.Application;

namespace Nodo.Quotes.Application;

public interface IQuotesUnitOfWork : IUnitOfWork;

public interface IQuotesOutbox : IOutbox;

// ---- Ports towards external contexts (implemented by adapters in Infrastructure, spec 05) ----

public sealed record HallInfo(Guid HallId, string Name, int MaxAttendance);

/// <summary>Own equipment. <see cref="CostPerEvent"/> is per unit and per event.</summary>
public sealed record EquipmentItem(string Code, string Name, int Stock, decimal CostPerEvent);

public sealed record CateringItem(string Code, string Name, decimal CostPerPerson);

/// <summary>Upstream "Espacios y Recursos" and the catering supplier as seen by Quotes (Customer/Supplier, OHS/PL).</summary>
public interface ISpacesCatalog
{
    Task<HallInfo?> GetHallAsync(Guid hallId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EquipmentItem>> GetEquipmentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CateringItem>> GetCateringMenuAsync(CancellationToken cancellationToken = default);
}

/// <summary>Taken as published by the upstream context (Conformist): the plan's discount and whether it is active.</summary>
public sealed record Membership(bool IsActiveMember, int DiscountPercentage);

public interface IMembershipDirectory
{
    Task<Membership> GetMembershipAsync(Guid customerId, CancellationToken cancellationToken = default);
}

public interface ICustomerDirectory
{
    Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>Read side implemented over the database with AsNoTracking (spec 04). Returns DTOs without enrichment.</summary>
public interface IQuoteQueries
{
    Task<QuoteDto?> GetAsync(Guid quoteId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<QuoteDto>> ListAsync(
        Guid? customerId, string? status, CancellationToken cancellationToken = default);
}
