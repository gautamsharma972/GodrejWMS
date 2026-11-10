using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.PlatformAdmin.Logs.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Logs.Queries;

/// <summary>Paginated, filterable raw entries for one day's log file - the entry-browser half of
/// the hidden Platform Admin log viewer, next to GetLogAnalyticsQuery. Platform-Admin-only.</summary>
public sealed record GetLogEntriesQuery(
    DateOnly Date,
    string? Level = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? EndDate = null) : IRequest<PaginatedList<LogEntryDto>>;

public sealed class GetLogEntriesValidator : AbstractValidator<GetLogEntriesQuery>
{
    public GetLogEntriesValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view this.");
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
        RuleFor(x => x)
            .Must(x => x.EndDate is null || x.EndDate >= x.Date)
            .WithMessage("The end date cannot be before the start date.");
        RuleFor(x => x)
            .Must(x => x.EndDate is null || x.EndDate.Value.DayNumber - x.Date.DayNumber <= 31)
            .WithMessage("A maximum of 32 days can be viewed at once.");
    }
}

public sealed class GetLogEntriesHandler(ILogArchiveReader reader)
    : IRequestHandler<GetLogEntriesQuery, PaginatedList<LogEntryDto>>
{
    public Task<PaginatedList<LogEntryDto>> Handle(GetLogEntriesQuery request, CancellationToken cancellationToken)
    {
        var endDate = request.EndDate ?? request.Date;
        IEnumerable<RawLogEntry> entries = EachDate(request.Date, endDate).SelectMany(reader.ReadEntries);

        if (!string.IsNullOrWhiteSpace(request.Level) && !string.Equals(request.Level, "All", StringComparison.OrdinalIgnoreCase))
        {
            entries = entries.Where(e => string.Equals(e.Level, request.Level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            entries = entries.Where(e => e.Message.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = entries.OrderByDescending(e => e.Timestamp).ToList();
        var page = ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new LogEntryDto(e.Timestamp, e.Level, e.Message))
            .ToList();

        var result = new PaginatedList<LogEntryDto>(page, ordered.Count, request.PageNumber, request.PageSize);
        return Task.FromResult(result);
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
