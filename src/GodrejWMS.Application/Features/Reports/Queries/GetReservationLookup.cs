using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Reservation Lookup: for a material code, a location and/or a GRN / Pick reference, lists the inward
/// put-aways (GRN) and pullout picks that currently hold it. By default only unconfirmed rows are returned - the
/// stock is "reserved" until the GRN put-away / pullout is confirmed - and rejected transactions are never shown.
/// At least one criterion is required so the screen never dumps every reservation.</summary>
public sealed record GetReservationLookupQuery(
    long? MaterialCode = null,
    string? LocationCode = null,
    string? GrnReference = null,
    string? PickReference = null,
    bool IncludeConfirmed = false) : IRequest<ReservationLookupResult>;

/// <summary>Recent references offered in the lookup's searchable dropdowns (hint = vehicle / LR / destination).</summary>
public sealed record ReservationReferenceOptionDto(string Reference, string? Hint);

public sealed record ReservationReferenceOptions(
    IReadOnlyList<ReservationReferenceOptionDto> Grns,
    IReadOnlyList<ReservationReferenceOptionDto> Picks);

public sealed record GetReservationReferenceOptionsQuery : IRequest<ReservationReferenceOptions>;

public sealed class GetReservationReferenceOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetReservationReferenceOptionsQuery, ReservationReferenceOptions>
{
    public const int MaxOptions = 1000;

    public async Task<ReservationReferenceOptions> Handle(GetReservationReferenceOptionsQuery request, CancellationToken cancellationToken)
    {
        var grns = await db.InwardTransactions.AsNoTracking()
            .Where(t => !t.IsRejected)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaxOptions)
            .Select(t => new { t.ReferenceNumber, t.VehicleNumber, t.LrNumber })
            .ToListAsync(cancellationToken);
        var picks = await db.PulloutTransactions.AsNoTracking()
            .Where(t => !t.IsRejected)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaxOptions)
            .Select(t => new { t.ReferenceNumber, t.VehicleNumber, t.ClientIndentNumber })
            .ToListAsync(cancellationToken);

        return new ReservationReferenceOptions(
            grns.Select(g => new ReservationReferenceOptionDto(g.ReferenceNumber, Join(g.VehicleNumber, g.LrNumber is null ? null : "LR " + g.LrNumber))).ToList(),
            picks.Select(p => new ReservationReferenceOptionDto(p.ReferenceNumber, Join(p.VehicleNumber, p.ClientIndentNumber))).ToList());
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        return text.Length == 0 ? null : text;
    }
}

public sealed record ReservationInboundRowDto(
    int TransactionId, string GrnNumber, string? LrNumber, string? VehicleNumber,
    long MaterialCode, string MaterialDescription, string LocationCode, int? MfgMonth,
    decimal QuantityBoxes, bool IsConfirmed, DateTimeOffset CreatedAt);

public sealed record ReservationOutboundRowDto(
    int TransactionId, string PickReference, string? IndentNumber, string? Destination, string? VehicleNumber,
    long MaterialCode, string MaterialDescription, string LocationCode, int MfgMonth,
    decimal QuantityBoxes, bool IsConfirmed, DateTimeOffset CreatedAt);

public sealed record ReservationLookupResult(
    IReadOnlyList<ReservationInboundRowDto> Inbound,
    IReadOnlyList<ReservationOutboundRowDto> Outbound,
    bool Truncated,
    bool HasCriteria);

public sealed class GetReservationLookupHandler(IApplicationDbContext db)
    : IRequestHandler<GetReservationLookupQuery, ReservationLookupResult>
{
    public const int MaxRowsPerSide = 500;

    public async Task<ReservationLookupResult> Handle(GetReservationLookupQuery request, CancellationToken cancellationToken)
    {
        var location = request.LocationCode?.Trim();
        var grnReference = request.GrnReference?.Trim();
        var pickReference = request.PickReference?.Trim();
        var material = request.MaterialCode;

        if (material is null && string.IsNullOrEmpty(location) && string.IsNullOrEmpty(grnReference) && string.IsNullOrEmpty(pickReference))
        {
            return new ReservationLookupResult([], [], false, false);
        }

        // A reference only narrows its own side; when it is the only criterion, the other side has nothing to show.
        var hasShared = material is not null || !string.IsNullOrEmpty(location);
        var showInbound = hasShared || !string.IsNullOrEmpty(grnReference) || string.IsNullOrEmpty(pickReference);
        var showOutbound = hasShared || !string.IsNullOrEmpty(pickReference) || string.IsNullOrEmpty(grnReference);

        var inboundQuery = db.InwardPutaways.AsNoTracking()
            .Where(p => !p.InwardTransactionLine.InwardTransaction.IsRejected);
        if (!request.IncludeConfirmed)
        {
            inboundQuery = inboundQuery.Where(p => !p.IsConfirmed);
        }
        if (material is not null)
        {
            var code = material.Value;
            inboundQuery = inboundQuery.Where(p => p.InwardTransactionLine.Material.MaterialNumber == code);
        }
        if (!string.IsNullOrEmpty(location))
        {
            inboundQuery = inboundQuery.Where(p => p.PalletPosition.LocationCode == location);
        }
        if (!string.IsNullOrEmpty(grnReference))
        {
            inboundQuery = inboundQuery.Where(p => p.InwardTransactionLine.InwardTransaction.ReferenceNumber.StartsWith(grnReference));
        }

        var outboundQuery = db.PulloutPicks.AsNoTracking()
            .Where(k => !k.PulloutTransactionLine.PulloutTransaction.IsRejected);
        if (!request.IncludeConfirmed)
        {
            outboundQuery = outboundQuery.Where(k => !k.PulloutTransactionLine.PulloutTransaction.IsConfirmed);
        }
        if (material is not null)
        {
            var code = material.Value;
            outboundQuery = outboundQuery.Where(k => k.PulloutTransactionLine.Material.MaterialNumber == code);
        }
        if (!string.IsNullOrEmpty(location))
        {
            outboundQuery = outboundQuery.Where(k => k.PalletPosition.LocationCode == location);
        }
        if (!string.IsNullOrEmpty(pickReference))
        {
            outboundQuery = outboundQuery.Where(k => k.PulloutTransactionLine.PulloutTransaction.ReferenceNumber.StartsWith(pickReference));
        }

        var inbound = !showInbound ? [] : await inboundQuery
            .OrderByDescending(p => p.InwardTransactionLine.InwardTransaction.CreatedAt)
            .ThenBy(p => p.PalletPosition.LocationCode)
            .Take(MaxRowsPerSide + 1)
            .Select(p => new ReservationInboundRowDto(
                p.InwardTransactionLine.InwardTransactionId,
                p.InwardTransactionLine.InwardTransaction.ReferenceNumber,
                p.InwardTransactionLine.InwardTransaction.LrNumber,
                p.InwardTransactionLine.InwardTransaction.VehicleNumber,
                p.InwardTransactionLine.Material.MaterialNumber,
                p.InwardTransactionLine.Material.Description,
                p.PalletPosition.LocationCode,
                p.InwardTransactionLine.MfgMonth,
                p.QuantityBoxes,
                p.IsConfirmed,
                p.InwardTransactionLine.InwardTransaction.CreatedAt))
            .ToListAsync(cancellationToken);

        var outbound = !showOutbound ? [] : await outboundQuery
            .OrderByDescending(k => k.PulloutTransactionLine.PulloutTransaction.CreatedAt)
            .ThenBy(k => k.PalletPosition.LocationCode)
            .Take(MaxRowsPerSide + 1)
            .Select(k => new ReservationOutboundRowDto(
                k.PulloutTransactionLine.PulloutTransactionId,
                k.PulloutTransactionLine.PulloutTransaction.ReferenceNumber,
                k.PulloutTransactionLine.PulloutTransaction.ClientIndentNumber,
                k.PulloutTransactionLine.PlantName,
                k.PulloutTransactionLine.PulloutTransaction.VehicleNumber,
                k.PulloutTransactionLine.Material.MaterialNumber,
                k.PulloutTransactionLine.Material.Description,
                k.PalletPosition.LocationCode,
                k.MfgMonth,
                k.QuantityBoxes,
                k.PulloutTransactionLine.PulloutTransaction.IsConfirmed,
                k.PulloutTransactionLine.PulloutTransaction.CreatedAt))
            .ToListAsync(cancellationToken);

        var truncated = inbound.Count > MaxRowsPerSide || outbound.Count > MaxRowsPerSide;
        return new ReservationLookupResult(
            inbound.Take(MaxRowsPerSide).ToList(), outbound.Take(MaxRowsPerSide).ToList(), truncated, true);
    }
}
