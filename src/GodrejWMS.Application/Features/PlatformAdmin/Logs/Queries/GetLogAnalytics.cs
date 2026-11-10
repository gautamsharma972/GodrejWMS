using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.PlatformAdmin.Logs.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Logs.Queries;

/// <summary>Level breakdown, hour-of-day histogram and the most frequent warnings/errors for one
/// day's log file - the "analytics" half of the hidden Platform Admin log viewer, next to the raw
/// entry browser in GetLogEntriesQuery. Platform-Admin-only.</summary>
public sealed record GetLogAnalyticsQuery(DateOnly Date, DateOnly? EndDate = null) : IRequest<LogAnalyticsDto>;

public sealed class GetLogAnalyticsValidator : AbstractValidator<GetLogAnalyticsQuery>
{
    public GetLogAnalyticsValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view this.");
        RuleFor(x => x)
            .Must(x => x.EndDate is null || x.EndDate >= x.Date)
            .WithMessage("The end date cannot be before the start date.");
        RuleFor(x => x)
            .Must(x => x.EndDate is null || x.EndDate.Value.DayNumber - x.Date.DayNumber <= 31)
            .WithMessage("A maximum of 32 days can be viewed at once.");
    }
}

public sealed class GetLogAnalyticsHandler(ILogArchiveReader reader)
    : IRequestHandler<GetLogAnalyticsQuery, LogAnalyticsDto>
{
    private const int TopIssueCount = 15;
    private const int MessageKeyMaxLength = 200;

    public Task<LogAnalyticsDto> Handle(GetLogAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var endDate = request.EndDate ?? request.Date;
        var entries = EachDate(request.Date, endDate)
            .SelectMany(reader.ReadEntries)
            .ToList();

        var levelCounts = entries
            .GroupBy(e => e.Level)
            .Select(g => new LogLevelCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var hourly = new int[24];
        foreach (var entry in entries)
        {
            hourly[entry.Timestamp.Hour]++;
        }

        var countsByDate = entries
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.Date))
            .ToDictionary(g => g.Key, g => g.Count());
        var daily = EachDate(request.Date, endDate)
            .Select(date => new LogDailyCountDto(date, countsByDate.GetValueOrDefault(date)))
            .ToList();

        var topIssues = entries
            .Where(e => e.Level is "WRN" or "ERR" or "FTL")
            .GroupBy(e => (e.Level, Key: FirstLine(e.Message)))
            .Select(g => new LogMessageFrequencyDto(g.Key.Level, g.Key.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(TopIssueCount)
            .ToList();

        var result = new LogAnalyticsDto(request.Date, endDate, entries.Count, levelCounts, hourly, daily, topIssues);
        return Task.FromResult(result);
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static string FirstLine(string message)
    {
        var newlineIndex = message.IndexOf('\n');
        var line = newlineIndex < 0 ? message : message[..newlineIndex];
        return line.Length > MessageKeyMaxLength ? line[..MessageKeyMaxLength] : line;
    }
}
