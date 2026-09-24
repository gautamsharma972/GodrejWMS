namespace GodrejWMS.Application.Features.Reports.Dtos;

public sealed record PulloutStockAgeRowDto(
    int TransactionId,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    long MaterialNumber,
    string MaterialDescription,
    string LocationCode,
    string MfgMonthLabel,
    decimal QuantityBoxes,
    double AgeDays);

public sealed record PulloutStockAgeSummaryDto(
    int PickCount,
    double? AverageAgeDays,
    double? MedianAgeDays,
    IReadOnlyList<PulloutStockAgeRowDto> Rows);

public sealed record PulloutLocationUtilizationRowDto(
    string ZoneCode,
    string ZoneName,
    string LocationTypeCode,
    string LocationTypeName,
    int PickCount,
    decimal TotalQuantityBoxes);

public sealed record PulloutExceptionRowDto(
    int TransactionId,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    decimal ShortfallBoxes,
    string Status,
    string? Remarks);
