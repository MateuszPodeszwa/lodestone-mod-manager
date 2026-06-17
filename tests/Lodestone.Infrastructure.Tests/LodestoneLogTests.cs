using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.Tests;

public class LodestoneLogTests
{
    [Fact]
    public void LatestLogFile_returns_null_when_the_directory_is_missing()
    {
        using var dir = new TempDir();

        LodestoneLog.LatestLogFile(dir.File("no-such-folder")).ShouldBeNull();
    }

    [Fact]
    public void LatestLogFile_returns_null_when_no_logs_have_been_written()
    {
        using var dir = new TempDir();

        LodestoneLog.LatestLogFile(dir.Path).ShouldBeNull();
    }

    [Fact]
    public void LatestLogFile_returns_the_most_recently_written_log()
    {
        using var dir = new TempDir();
        string older = dir.File("lodestone-20260101.log");
        string newer = dir.File("lodestone-20260102.log");
        File.WriteAllText(older, "old");
        File.WriteAllText(newer, "new");
        // Pin the timestamps so the result is deterministic regardless of write order.
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        LodestoneLog.LatestLogFile(dir.Path).ShouldBe(newer);
    }

    [Fact]
    public void LatestLogFile_ignores_files_that_are_not_lodestone_logs()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("notes.txt"), "x");
        File.WriteAllText(dir.File("crash-report.md"), "x");

        LodestoneLog.LatestLogFile(dir.Path).ShouldBeNull();
    }
}
