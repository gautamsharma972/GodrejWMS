using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.MaterialConversions.Dtos;

/// <summary>One stock cell of the old code proposed for conversion (FIFO: oldest PKM first).</summary>
public sealed record ConversionProposalLineDto(
    int StockBatchId, int PalletPositionId, string LocationCode, int MfgMonth, string MfgMonthLabel, int StockSubtypeId,
    decimal AvailableBoxes, decimal QuantityBoxes);

public sealed record ConversionProposalDto(
    int FromMaterialId, long FromMaterialNumber, string FromDescription,
    int ToMaterialId, long ToMaterialNumber, string ToDescription,
    decimal AvailableBoxes, decimal ProposedBoxes,
    IReadOnlyList<ConversionProposalLineDto> Lines,
    IReadOnlyList<string> Warnings);

public sealed record MaterialConversionLineDto(
    int Id, int StockBatchId, string LocationCode, int MfgMonth, string MfgMonthLabel, decimal QuantityBoxes, decimal SystemQuantityAtRequest);

public sealed record MaterialConversionDto(
    int Id, string ConversionNumber, MaterialConversionStatus Status,
    long FromMaterialNumber, string FromDescription, long ToMaterialNumber, string ToDescription,
    decimal TotalBoxes, int LineCount, string? ReasonText,
    string? RequestedByUserName, DateTimeOffset RequestedAt,
    string? ReviewedByUserName, DateTimeOffset? ReviewedAt, string? ReviewComment,
    IReadOnlyList<MaterialConversionLineDto> Lines);
