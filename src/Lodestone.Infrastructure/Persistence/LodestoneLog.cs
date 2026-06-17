using System.Globalization;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>
/// Tiny append-only file logger to <c>%AppData%/Lodestone/logs</c>. Deliberately dependency-free and
/// best-effort (never throws) so it can be called from anywhere — including the global crash handler —
/// without becoming a failure source itself.
/// </summary>
public static class LodestoneLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    /// <summary>The most recently written log file, or <c>null</c> when none exist yet. Lets the UI reveal
    /// the newest log — the one most likely needed for a bug report — instead of just the folder.</summary>
    public static string? LatestLogFile() => LatestLogFile(LodestonePaths.LogsDirectory);

    /// <summary>Finds the newest <c>lodestone-*.log</c> in <paramref name="directory"/>. Exposed as an
    /// overload taking an explicit directory so the lookup can be unit-tested in isolation; the
    /// parameterless version uses the canonical <see cref="LodestonePaths.LogsDirectory"/>.</summary>
    public static string? LatestLogFile(string directory)
    {
        try
        {
            var dir = new DirectoryInfo(directory);
            if (!dir.Exists)
            {
                return null;
            }

            return dir.EnumerateFiles("lodestone-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .FirstOrDefault()?.FullName;
        }
        catch (IOException)
        {
            return null; // best-effort: never let a log lookup throw into the UI
        }
    }

    private static void Write(string level, string text)
    {
        try
        {
            Directory.CreateDirectory(LodestonePaths.LogsDirectory);
            string file = Path.Combine(LodestonePaths.LogsDirectory, $"lodestone-{DateTime.UtcNow:yyyyMMdd}.log");
            string line = $"{DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} [{level}] {text}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(file, line);
            }
        }
        catch (IOException)
        {
            // logging must never throw
        }
    }
}
