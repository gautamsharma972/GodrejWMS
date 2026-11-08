using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>The Picker Put-away list: every GRN (with or without PKM) that still has a put-away location to be worked.</summary>
public sealed record GetPickerPendingInwardQuery(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 10) : IRequest<PaginatedList<InwardHistoryDto>>;

public sealed class GetPickerPendingInwardHandler(IApplicationDbContext db)
    : IRequestHandler<GetPickerPendingInwardQuery, PaginatedList<InwardHistoryDto>>
{
    public Task<PaginatedList<InwardHistoryDto>> Handle(GetPickerPendingInwardQuery request, CancellationToken cancellationToken)
    {
        var transactions = db.InwardTransactions.AsNoTracking()
            // Needs the picker: an open reservation, or - for an order posted on upload - a location the picker has not confirmed
            // or changed yet. (Only an order still pending from before posting on upload keeps a GIT / NO RACK entry open
            // for correction until the supervisor confirms it.)
            .Where(t => !t.IsRejected
                && t.Lines.Any(l => l.Putaways.Any(p => !p.IsConfirmed
                    || (t.PostedAt != null && !p.IsPickerPlaced && p.ExcludedLocation == null)
                    || (t.PostedAt == null && p.ExcludedLocation != null && !p.IsExcludedLocked))));

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
                VirtualLocationCount: t.Lines.SelectMany(l => l.Putaways).Count(p => p.PalletPosition.IsVirtual)));

        return PaginatedList<InwardHistoryDto>.CreateAsync(query, request.PageNumber, request.PageSize);
    }
}
