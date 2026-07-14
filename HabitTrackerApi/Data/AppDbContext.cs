using Microsoft.EntityFrameworkCore;
using Models;

namespace Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Habit> Habits { get; set; }

    public DbSet<HabitCompletion> HabitCompletions { get; set; }

}