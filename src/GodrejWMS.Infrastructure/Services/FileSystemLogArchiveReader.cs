using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>Reads the rolling Serilog file sink's output straight off disk. Resolves the same
/// directory/file-name pattern Serilog itself resolves from Serilog:WriteTo:File:Args:path
/// (relative to the process's current directory, same as Serilog), so this never drifts from
/// wherever the app is actually writing on a given host.</summary>
public sealed class FileSystemLogArchiveReader : ILogArchiveReader
{
    private static readonly Regex EntryStartRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<level>\w{3})\] (?<msg>.*)$",
        RegexOptions.Compiled);

    private readonly string _directory;
    private readonly string _prefix;
    private readonly string _extension;

    public FileSystemLogArchiveReader(IConfiguration configuration)
    {
        var pathPattern = ResolvePathPattern(configuration);
        var fileName = Path.GetFileName(pathPattern);
        _extension = Path.GetExtension(fileName);
        _prefix = fileName[..^_extension.Length];

        var directory = Path.GetDirectoryName(pathPattern);
        _directory = string.IsNullOrEmpty(directory)
            ? Directory.GetCurrentDirectory()
            : Path.IsPathRooted(directory) ? directory : Path.Combine(Directory.GetCurrentDirectory(), directory);
    }

    private static string ResolvePathPattern(IConfiguration configuration)
    {
        var fileSink = configuration.GetSection("Serilog:WriteTo").GetChildren()
            .FirstOrDefault(s => string.Equals(s["Name"], "File", StringComparison.OrdinalIgnoreCase));
        return fileSink?["Args:path"] ?? "Logs/godrejwms-.log";
    }

    public IReadOnlyList<LogFileInfo> ListLogFiles()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var results = new List<LogFileInfo>();
        foreach (var path in Directory.EnumerateFiles(_directory, $"{_prefix}*{_extension}"))
        {
            var name = Path.GetFileName(path);
            if (name.Length <= _prefix.Length + _extension.Length)
            {
                continue;
            }

            var datePart = name[_prefix.Length..^_extension.Length];
            if (DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                results.Add(new LogFileInfo(date, name, new FileInfo(path).Length));
            }
        }

        return results;
    }

    public IReadOnlyList<RawLogEntry> ReadEntries(DateOnly date)
    {
        var path = Path.Combine(_directory, $"{_prefix}{date:yyyyMMdd}{_extension}");
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<RawLogEntry>();
        DateTimeOffset? currentTimestamp = null;
        string? currentLevel = null;
        StringBuilder? currentMessage = null;

        void Flush()
        {
            if (currentTimestamp is not null && currentMessage is not null)
            {
                entries.Add(new RawLogEntry(currentTimestamp.Value, currentLevel ?? "INF", currentMessage.ToString()));
            }
        }

        foreach (var line in File.ReadLines(path))
        {
            var match = EntryStartRegex.Match(line);
            if (match.Success)
            {
                Flush();
                currentTimestamp = DateTimeOffset.ParseExact(
                    match.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                currentLevel = match.Groups["level"].Value;
                currentMessage = new StringBuilder(match.Groups["msg"].Value);
            }
            else if (currentMessage is not null)
            {
                currentMessage.Append('\n').Append(line);
            }
        }

        Flush();
        return entries;
    }
}
