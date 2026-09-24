namespace GodrejWMS.Application.Features.Reports.Dtos;

public sealed record MovementReasonBreakdownRowDto(
    int ReasonId,
    string ReasonCode,
    string ReasonName,
    int MovementCount,
    decimal TotalQuantityBoxes);

public sealed record MovementUserActivityRowDto(
    string UserName,
    int MovementCount,
    decimal TotalQuantityBoxes);

public sealed record MovementLocationChurnRowDto(
    string LocationCode,
    int MovesOut,
    int MovesIn,
    int TotalMoves,
    decimal QuantityMovedOut,
    decimal QuantityMovedIn);
