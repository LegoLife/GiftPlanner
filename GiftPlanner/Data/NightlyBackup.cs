using Microsoft.Data.Sqlite;

namespace GiftPlanner.Data;

/// <summary>
/// Copies the database to {data}/backups/giftplanner-YYYY-MM-DD.db once a day (at startup if today's is missing,
/// then around 3 AM) and keeps the newest <see cref="Keep"/> (two days). Uses SQLite's online backup, so it's safe while the app runs.
/// </summary>
public sealed class NightlyBackup(string dbPath, string backupDir, ILogger<NightlyBackup> log, TimeProvider clock) : BackgroundService
{
    public const int Keep = 2;
    private static readonly TimeSpan RunAt = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                BackupNow(DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                log.LogError(ex, "Nightly backup failed");
            }

            var now = clock.GetLocalNow();
            var next = now.Date + RunAt;
            if (next <= now) next = next.AddDays(1);
            try { await Task.Delay(next - now, clock, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    /// <returns>The backup file path, or null if today's backup already existed.</returns>
    public string? BackupNow(DateOnly today)
    {
        if (!File.Exists(dbPath)) return null;
        Directory.CreateDirectory(backupDir);
        var target = Path.Combine(backupDir, $"giftplanner-{today:yyyy-MM-dd}.db");
        if (File.Exists(target)) return null;

        var temp = target + ".tmp";
        using (var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
        using (var dest = new SqliteConnection($"Data Source={temp};Pooling=False"))
        {
            source.Open();
            dest.Open();
            source.BackupDatabase(dest);
        }
        File.Move(temp, target, overwrite: true);
        log.LogInformation("Backed up database to {Path}", target);

        foreach (var old in Directory.GetFiles(backupDir, "giftplanner-*.db").Order().Reverse().Skip(Keep))
            File.Delete(old);
        return target;
    }
}
