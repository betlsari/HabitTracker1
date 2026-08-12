using Microsoft.EntityFrameworkCore;
using Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Data;

public class AppDbContext : IdentityDbContext<User>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Habit> Habits { get; set; }

    public DbSet<HabitCompletion> HabitCompletions { get; set; }

}