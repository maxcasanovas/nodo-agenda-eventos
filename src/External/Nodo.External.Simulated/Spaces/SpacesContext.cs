using System.Text.Json.Serialization;

namespace Nodo.External.Simulated.Spaces;

// "Espacios y Recursos" speaks its own language: venues, assets and catalog items with snake_case names.
// Our modules translate it through their SpacesCatalogAdapter (Customer/Supplier with OHS/PL).

public sealed record AssetDto(
    [property: JsonPropertyName("asset_code")] string AssetCode,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("units_in_stock")] int UnitsInStock,
    [property: JsonPropertyName("fee_per_event")] decimal FeePerEvent);

public sealed record VenueDto(
    [property: JsonPropertyName("venue_uid")] string VenueUid,
    [property: JsonPropertyName("venue_code")] string VenueCode,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("max_capacity")] int MaxCapacity,
    [property: JsonPropertyName("opens_at")] string OpensAt,
    [property: JsonPropertyName("closes_at")] string ClosesAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<AssetDto> Assets);

/// <summary>An item of the subcontracted caterer's menu.</summary>
public sealed record CateringItemDto(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("price_per_head")] decimal PricePerHead);

public sealed class SpacesContext
{
    private static readonly VenueDto Polivalente = new(
        "11111111-1111-1111-1111-111111111111", "SALON-POLIVALENTE", "Salón Polivalente", 80, "07:00", "23:00",
    [
        new("PROJECTOR", "Proyector", 1, 150m),
        new("TV", "Pantalla TV 55\"", 2, 100m),
        new("SOUND", "Sistema de sonido", 1, 250m),
        new("LIGHTS", "Kit de luces", 1, 200m)
    ]);

    private static readonly IReadOnlyList<CateringItemDto> Menu =
    [
        new("COFFEE_BREAK", "Coffee break", 35m),
        new("SNACK", "Refrigerio", 25m),
        new("LUNCH", "Almuerzo ejecutivo", 90m),
        new("BUFFET", "Cena buffet", 120m)
    ];

    public IReadOnlyList<VenueDto> Venues { get; } = [Polivalente];

    public IReadOnlyList<CateringItemDto> CateringMenu => Menu;

    public VenueDto? FindVenue(string venueUid) =>
        Venues.FirstOrDefault(v => string.Equals(v.VenueUid, venueUid, StringComparison.OrdinalIgnoreCase));
}
