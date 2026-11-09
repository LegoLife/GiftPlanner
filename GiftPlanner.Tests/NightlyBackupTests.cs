using GiftPlanner.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GiftPlanner.Tests;

public sealed class NightlyBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gp-backup-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;
    private readonly NightlyBackup _backup;

    public NightlyBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "giftplanner.db");
        using (var c = new SqliteConnection($"Data Source={_db};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE T (X TEXT); INSERT INTO T VALUES ('hello');";
            cmd.ExecuteNonQuery();
        }
        _backup = new NightlyBackup(_db, Path.Combine(_dir, "backups"), NullLogger<NightlyBackup>.Instance, TimeProvider.System);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Creates_a_readable_copy_once_per_day()
    {
        var path = _backup.BackupNow(new DateOnly(2026, 12, 1));
        Assert.NotNull(path);
        Assert.EndsWith("giftplanner-2026-12-01.db", path);
        Assert.Null(_backup.BackupNow(new DateOnly(2026, 12, 1))); // second run same day does nothing

        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT X FROM T";
        Assert.Equal("hello", cmd.ExecuteScalar());
    }

    [Fact]
    public void Keeps_only_the_newest_two()
    {
        var start = new DateOnly(2026, 12, 1);
        for (var d = 0; d < 20; d++) _backup.BackupNow(start.AddDays(d));

        var files = Directory.GetFiles(Path.Combine(_dir, "backups")).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(NightlyBackup.Keep, files.Count);
        Assert.Equal("giftplanner-2026-12-19.db", files[0]);
        Assert.Equal("giftplanner-2026-12-20.db", files[^1]);
    }
}
