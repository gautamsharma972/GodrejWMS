namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>One rolling Serilog log file on disk for a given calendar day.</summary>
public sealed record LogFileInfo(DateOnly Date, string FileName, long SizeBytes);

/// <summary>One parsed log entry (a Serilog line plus any continuation/exception lines that
/// followed it, joined with '\n').</summary>
public sealed record RawLogEntry(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>Reads the same rolling Serilog file archive (configured under Serilog:WriteTo:File in
/// appsettings) that ops already tails by hand on the server - nothing new is written, this only
/// reads what's already there. The only callers are the Platform-Admin-only log viewer
/// (GetLogFileDatesQuery / GetLogAnalyticsQuery / GetLogEntriesQuery) - never exposed to any other
/// role.</summary>
public interface ILogArchiveReader
{
    IReadOnlyList<LogFileInfo> ListLogFiles();

    IReadOnlyList<RawLogEntry> ReadEntries(DateOnly date);
}
