namespace GodrejWMS.Application.Features.Locations.Dtos;

public sealed record LocationImportRow(
    string LocationCode,
    string? FlatLabel,
    string LocationTypeCode,
    string LocationSubtypeCode,
    string ZoneTypeCode,
    int DistancePriority,
    int MaxPallets,
    bool IsActive,
    int RowNumber);

public sealed record ImportLocationsResult(int Updated, IReadOnlyList<string> Errors);
