using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>The Picker Put-away list: every GRN (with or without PKM) that still has a put-away location to be worked.</summary>
public sealed record GetPickerPendingInwardQuery(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 10) : IRequest<PaginatedList<InwardHistoryDto>>;

public sealed class GetPickerPendingInwardHandler(IApplicationDbContext db, IPermissionService permissions)
    : IRequestHandler<GetPickerPendingInwardQuery, PaginatedList<InwardHistoryDto>>
{
    public async Task<PaginatedList<InwardHistoryDto>> Handle(GetPickerPendingInwardQuery request, CancellationToken cancellationToken)
    {
        // A supervisor (Inward.Confirm) sees every GRN that is not confirmed yet - submitted or not - because the final confirm is theirs.
        // A picker only sees GRNs that still have a location to submit: once everything is submitted the GRN waits for the supervisor.
        var reviewer = await permissions.HasPermissionAsync(PermissionKeys.Inward.Confirm, cancellationToken);
        var open = db.InwardTransactions.AsNoTracking().Where(t => !t.IsRejected);
        var transactions = reviewer
            // An open reservation, or - for an order posted on upload - a location that is not confirmed (locked) yet.
            ? open.Where(t => t.Lines.Any(l => l.Putaways.Any(p => p.QuantityBoxes > 0 && !p.IsExcludedLocked
                && (!p.IsConfirmed || t.PostedAt != null || p.ExcludedLocation != null))))
            : open.Where(t => t.Lines.Any(l => l.Putaways.Any(p => p.QuantityBoxes > 0 && !p.IsExcludedLocked && !p.IsPickerPlaced && p.ExcludedLocation == null
                && (!p.IsConfirmed || t.PostedAt != null))));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            transactions = transactions.Where(t =>
                t.ReferenceNumber.Contains(term) ||
                (t.VehicleNumber != null && t.VehicleNumber.Contains(term)) ||
                (t.LrNumber != null && t.LrNumber.Contains(term)) ||
                t.Lines.Any(l => l.Material.MaterialNumber.ToString().Contains(term) || l.Material.Description.Contains(term)));
        }

        var query = transactions
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new InwardHistoryDto(
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                t.Lines.Count,
                t.Lines.Sum(l => (decimal?)l.AllocatedQuantityBoxes) ?? 0m,
                (AllocationStatus)(t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed),
                false,
                t.IsRejected,
                IsPkmless: t.IsPkmless,
                VehicleNumber: t.VehicleNumber,
                LrNumber: t.LrNumber,
                LocationCount: t.Lines.SelectMany(l => l.Putaways).Count(),
                SubmittedLocationCount: t.Lines.SelectMany(l => l.Putaways).Count(p => p.IsPickerPlaced || (p.IsConfirmed && t.PostedAt == null)),
                VirtualLocationCount: t.Lines.SelectMany(l => l.Putaways).Count(p => p.PalletPosition.IsVirtual),
                InboundBoxes: t.Lines.Sum(l => (decimal?)l.RequestedQuantityBoxes) ?? 0m,
                RecordedBoxes: db.InwardLineVariances.Where(v => v.InwardTransactionId == t.Id && v.Type != InwardVarianceType.Excess).Sum(v => (decimal?)v.QuantityBoxes) ?? 0m,
                ExcessBoxes: db.InwardLineVariances.Where(v => v.InwardTransactionId == t.Id && v.Type == InwardVarianceType.Excess).Sum(v => (decimal?)v.QuantityBoxes) ?? 0m));

        return await PaginatedList<InwardHistoryDto>.CreateAsync(query, request.PageNumber, request.PageSize);
    }
}
