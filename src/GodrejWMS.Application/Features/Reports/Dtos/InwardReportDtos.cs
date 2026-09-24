namespace GodrejWMS.Application.Features.Reports.Dtos;

public sealed record InwardRejectionReasonRowDto(
    string Reason,
    int GrnCount,
    decimal TotalRequestedBoxes);

public sealed record InwardTurnaroundRowDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    double? TurnaroundHours);

public sealed record InwardTurnaroundSummaryDto(
    int GrnCount,
    int ConfirmedCount,
    double? AverageHours,
    double? MedianHours,
    IReadOnlyList<InwardTurnaroundRowDto> Rows);

public sealed record InwardExceptionRowDto(
    int TransactionId,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    long MaterialNumber,
    string MaterialDescription,
    string MfgMonthLabel,
    decimal RequestedQuantityBoxes,
    decimal AllocatedQuantityBoxes,
    decimal ShortfallBoxes,
    string Status,
    string? Remarks);
