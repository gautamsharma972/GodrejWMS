using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.ActivityLogs.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.ActivityLogs.Queries;

public sealed record GetActivityLogsQuery(
    string? Search = null,
    string? Action = null,
    string? EntityName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string SortBy = "occurred",
    bool SortDescending = true,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PaginatedList<ActivityLogDto>>;

public sealed class GetActivityLogsHandler(IApplicationDbContext db)
    : IRequestHandler<GetActivityLogsQuery, PaginatedList<ActivityLogDto>>
{
    public Task<PaginatedList<ActivityLogDto>> Handle(GetActivityLogsQuery request, CancellationToken cancellationToken)
    {
        var query = db.ActivityLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(a =>
                a.Summary.Contains(term) ||
                a.EntityName.Contains(term) ||
                (a.EntityId != null && a.EntityId.Contains(term)) ||
                (a.UserName != null && a.UserName.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            query = query.Where(a => a.Action == request.Action);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityName))
        {
            query = query.Where(a => a.EntityName == request.EntityName);
        }

        if (request.From.HasValue)
        {
            query = query.Where(a => a.OccurredAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(a => a.OccurredAt < request.To.Value);
        }

        query = (request.SortBy, request.SortDescending) switch
        {
            ("user", false) => query.OrderBy(a => a.UserName).ThenByDescending(a => a.OccurredAt),
            ("user", true) => query.OrderByDescending(a => a.UserName).ThenByDescending(a => a.OccurredAt),
            ("action", false) => query.OrderBy(a => a.Action).ThenByDescending(a => a.OccurredAt),
            ("action", true) => query.OrderByDescending(a => a.Action).ThenByDescending(a => a.OccurredAt),
            ("entity", false) => query.OrderBy(a => a.EntityName).ThenByDescending(a => a.OccurredAt),
            ("entity", true) => query.OrderByDescending(a => a.EntityName).ThenByDescending(a => a.OccurredAt),
            ("occurred", false) => query.OrderBy(a => a.OccurredAt),
            _ => query.OrderByDescending(a => a.OccurredAt)
        };

        var projected = query.Select(a => new ActivityLogDto(
            a.Id,
            a.OccurredAt,
            a.UserName,
            a.Action,
            a.EntityName,
            a.EntityId,
            a.Summary,
            a.OldValuesJson,
            a.NewValuesJson));

        return PaginatedList<ActivityLogDto>.CreateAsync(projected, request.PageNumber, request.PageSize);
    }
}
