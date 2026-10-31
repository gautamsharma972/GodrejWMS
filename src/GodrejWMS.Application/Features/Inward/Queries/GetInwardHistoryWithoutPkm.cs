using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Queries;

/// <summary>The Pickers-only history view: only still-pending (not confirmed, not rejected) GRNs
/// submitted through the "Inward without PKM" flow - deliberately separate from
/// <see cref="GetInwardHistoryQuery"/> (which keeps showing every GRN, PKM-less or not, to every
/// other role unchanged) rather than a parameter on it.</summary>
public sealed record GetInwardHistoryWithoutPkmQuery(
    string? Search = null,
    int? WarehouseId = null,
    string SortBy = "created",
    bool SortDescending = true,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedList<InwardHistoryDto>>;

public sealed class GetInwardHistoryWithoutPkmHandler(IApplicationDbContext db)
    : IRequestHandler<GetInwardHistoryWithoutPkmQuery, PaginatedList<InwardHistoryDto>>
{
    public Task<PaginatedList<InwardHistoryDto>> Handle(GetInwardHistoryWithoutPkmQuery request, CancellationToken cancellationToken)
    {
        var transactions = db.InwardTransactions.AsNoTracking()
            .Where(t => t.IsPkmless && !t.IsRejected
                && !(t.Lines.SelectMany(l => l.Putaways).Any() && t.Lines.SelectMany(l => l.Putaways).All(p => p.IsConfirmed)));

        if (request.WarehouseId.HasValue)
        {
            transactions = transactions.Where(t => t.WarehouseId == request.WarehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            transactions = transactions.Where(t =>
                t.ReferenceNumber.Contains(term) ||
                t.Lines.Any(l =>
                    l.Material.MaterialNumber.ToString().Contains(term) ||
                    l.Material.Description.Contains(term)));
        }

        transactions = (request.SortBy, request.SortDescending) switch
        {
            ("reference", false) => transactions.OrderBy(t => t.ReferenceNumber),
            ("reference", true) => transactions.OrderByDescending(t => t.ReferenceNumber),
            ("lines", false) => transactions.OrderBy(t => t.Lines.Count()).ThenByDescending(t => t.CreatedAt),
            ("lines", true) => transactions.OrderByDescending(t => t.Lines.Count()).ThenByDescending(t => t.CreatedAt),
            ("quantity", false) => transactions.OrderBy(t => t.Lines.Sum(l => (decimal?)l.AllocatedQuantityBoxes) ?? 0m).ThenByDescending(t => t.CreatedAt),
            ("quantity", true) => transactions.OrderByDescending(t => t.Lines.Sum(l => (decimal?)l.AllocatedQuantityBoxes) ?? 0m).ThenByDescending(t => t.CreatedAt),
            ("created", false) => transactions.OrderBy(t => t.CreatedAt),
            _ => transactions.OrderByDescending(t => t.CreatedAt)
        };

        var query = transactions.Select(t => new InwardHistoryDto(
            t.Id,
            t.ReferenceNumber,
            t.CreatedAt,
            t.Lines.Count,
            t.Lines.Sum(l => (decimal?)l.AllocatedQuantityBoxes) ?? 0m,
            (AllocationStatus)(t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed),
            false, // these GRNs are pending by construction of the query above
            t.IsRejected,
            IsPkmless: true));

        return PaginatedList<InwardHistoryDto>.CreateAsync(query, request.PageNumber, request.PageSize);
    }
}
