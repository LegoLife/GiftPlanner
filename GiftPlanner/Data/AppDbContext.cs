using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Kid> Kids => Set<Kid>();
    public DbSet<GiverGroup> Groups => Set<GiverGroup>();
    public DbSet<Gift> Gifts => Set<Gift>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(a =>
        {
            a.HasIndex(x => x.NormalizedEmail).IsUnique();
            a.HasIndex(x => x.GoogleSubject).IsUnique();
        });
        b.Entity<Kid>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GiverGroup>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Gift>().HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<GiverGroup>().HasIndex(g => g.ShareToken).IsUnique();

        b.Entity<Gift>(g =>
        {
            g.Ignore(x => x.Store);
            g.Property(x => x.Price).HasConversion<double?>(); // SQLite can't sort/sum decimal
            g.HasMany(x => x.Kids).WithMany(x => x.Gifts).UsingEntity("GiftKid");
            g.HasMany(x => x.Groups).WithMany(x => x.Gifts).UsingEntity("GiftGroup");
            // Deleting the group that bought it leaves the gift bought, just without "by whom".
            g.HasOne(x => x.PurchasedByGroup).WithMany().HasForeignKey(x => x.PurchasedByGroupId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
