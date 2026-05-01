using Digger.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Digger.Data.Context;

public class DiggerContext : DbContext
{
    public DbSet<Movie> Movies { get; set; }

    public DiggerContext(DbContextOptions<DiggerContext> options)
        : base(options)
    {
        Database.EnsureCreated();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Movie>().Property((Movie e) => e.LastKnownStatus).HasConversion<string>();
    }
}
