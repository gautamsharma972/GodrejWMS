using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

public sealed record GetPulloutHistoryQuery(
    string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    AllocationStatus? Status = null,
    string SortBy = "created",
    bool SortDescending = true,
    int PageNumber = 1,
    int PageSize = 20,
    bool? IsConfirmed = null,
    bool? IsRejected = null) : IRequest<PaginatedList<PulloutHistoryDto>>;

public sealed class GetPulloutHistoryHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutHistoryQuery, PaginatedList<PulloutHistoryDto>>
{
    public Task<PaginatedList<PulloutHistoryDto>> Handle(GetPulloutHistoryQuery request, CancellationToken cancellationToken)
    {
        var transactions = db.PulloutTransactions.AsNoTracking();
        // "Not confirmed" means still pending: a rejected pullout is neither confirmed nor pending.
        if (request.IsConfirmed.HasValue)
            transactions = request.IsConfirmed.Value
                ? transactions.Where(t => t.IsConfirmed)
                : transactions.Where(t => !t.IsConfirmed && !t.IsRejected);
        if (request.IsRejected == true)
            transactions = transactions.Where(t => t.IsRejected);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            transactions = transactions.Where(t =>
                t.ReferenceNumber.Contains(term) ||
                t.Lines.Any(l =>
                    l.Material.MaterialNumber.ToString().Contains(term) ||
                    l.Material.Description.Contains(term)));
        }

        if (request.From.HasValue)
        {
            transactions = transactions.Where(t => t.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            transactions = transactions.Where(t => t.CreatedAt < request.To.Value);
        }

        if (request.Status.HasValue)
        {
            var status = (int)request.Status.Value;
            transactions = transactions.Where(t =>
                (t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed) == status);
        }

        transactions = (request.SortBy, request.SortDescending) switch
        {
            ("reference", false) => transactions.OrderBy(t => t.ReferenceNumber),
            ("reference", true) => transactions.OrderByDescending(t => t.ReferenceNumber),
            ("lines", false) => transactions.OrderBy(t => t.Lines.Count()).ThenByDescending(t => t.CreatedAt),
            ("lines", true) => transactions.OrderByDescending(t => t.Lines.Count()).ThenByDescending(t => t.CreatedAt),
            ("quantity", false) => transactions.OrderBy(t => t.Lines.Sum(l => (decimal?)l.PickedQuantityBoxes) ?? 0m).ThenByDescending(t => t.CreatedAt),
            ("quantity", true) => transactions.OrderByDescending(t => t.Lines.Sum(l => (decimal?)l.PickedQuantityBoxes) ?? 0m).ThenByDescending(t => t.CreatedAt),
            ("status", false) => transactions.OrderBy(t => t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed).ThenByDescending(t => t.CreatedAt),
            ("status", true) => transactions.OrderByDescending(t => t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed).ThenByDescending(t => t.CreatedAt),
            ("created", false) => transactions.OrderBy(t => t.CreatedAt),
            _ => transactions.OrderByDescending(t => t.CreatedAt)
        };

        var query = transactions.Select(t => new PulloutHistoryDto(
                t.Id,
                t.ReferenceNumber,
                t.CreatedAt,
                t.Lines.Count,
                t.Lines.Sum(l => (decimal?)l.PickedQuantityBoxes) ?? 0m,
                (AllocationStatus)(t.Lines.Max(l => (int?)l.Status) ?? (int)AllocationStatus.Failed),
                t.IsConfirmed,
                t.IsRejected));

        return PaginatedList<PulloutHistoryDto>.CreateAsync(query, request.PageNumber, request.PageSize);
    }
}
