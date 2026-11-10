namespace GodrejWMS.Application.Features.PlatformAdmin.Logs.Dtos;

public sealed record LogFileSummaryDto(DateOnly Date, long SizeBytes);

public sealed record LogLevelCountDto(string Level, int Count);

public sealed record LogMessageFrequencyDto(string Level, string Message, int Count);

public sealed record LogDailyCountDto(DateOnly Date, int Count);

public sealed record LogAnalyticsDto(
    DateOnly Date,
    DateOnly EndDate,
    int TotalEntries,
    IReadOnlyList<LogLevelCountDto> LevelCounts,
    IReadOnlyList<int> HourlyCounts,
    IReadOnlyList<LogDailyCountDto> DailyCounts,
    IReadOnlyList<LogMessageFrequencyDto> TopWarningsAndErrors);

public sealed record LogEntryDto(DateTimeOffset Timestamp, string Level, string Message);
