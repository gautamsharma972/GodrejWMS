using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Pullout.Dtos;

/// <summary>One parsed row from a "Format inventory pullout-upload" Excel: Material Code, Total Stock in CFB.</summary>
public sealed record PulloutImportRow(long MaterialCode, decimal QuantityBoxes, int RowNumber);

public sealed record PulloutPickDto(string LocationCode, string MfgMonthLabel, decimal QuantityBoxes);

public sealed record PulloutLineResultDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<PulloutPickDto> Picks);

public sealed record PulloutResultDto(
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutLineResultDto> Lines);

public sealed record PulloutHistoryDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    int LineCount,
    decimal TotalQuantityBoxes,
    AllocationStatus Status,
    bool IsConfirmed = true);

public sealed record PulloutDetailLineDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<PulloutPickDto> Picks);

public sealed record PulloutDetailDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutDetailLineDto> Lines,
    bool IsConfirmed = true);
