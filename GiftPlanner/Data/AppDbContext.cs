using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Kid> Kids => Set<Kid>();
    public DbSet<GiverGroup> Groups => Set<GiverGroup>();
    public DbSet<Gift> Gifts => Set<Gift>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<GiverGroup>().HasIndex(g => g.ShareToken).IsUnique();

        b.Entity<Gift>(g =>
        {
            g.Ignore(x => x.Store);
            g.Property(x => x.Price).HasConversion<double?>(); // SQLite can't sort/sum decimal
            // Deleting a group unassigns its gifts rather than deleting them.
            g.HasOne(x => x.Group).WithMany(x => x.Gifts).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
            g.HasOne(x => x.Kid).WithMany(x => x.Gifts).HasForeignKey(x => x.KidId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
