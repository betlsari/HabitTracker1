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

    public DbSet<Pet> Pets { get; set; }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<Badge> Badges { get; set; }

    public DbSet<UserBadge> UserBadges { get; set; }

    public DbSet<Flower> Flowers { get; set; }

    public DbSet<UserNotification> UserNotifications { get; set; }

    public DbSet<DeviceToken> DeviceTokens { get; set; }

    
    public DbSet<Book> Books { get; set; }

    public DbSet<BookReadingLog> BookReadingLogs { get; set; }
    public DbSet<PetAccessoryUnlock> PetAccessoryUnlocks { get; set; }
public DbSet<UserBackgroundUnlock> UserBackgroundUnlocks { get; set; }

    public DbSet<AuthAuditEvent> AuthAuditEvents { get; set; }
    public DbSet<TwoFactorAttempt> TwoFactorAttempts { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<User>(entity =>
        {
            entity.Property(u => u.TimeZoneId)
                .HasMaxLength(100)
                .HasDefaultValue("Europe/Istanbul");
        });
        builder.Entity<Habit>(entity =>
{
    entity.HasIndex(h => new { h.UserId, h.NormalizedName }).IsUnique();
});
        builder.Entity<RefreshToken>(entity =>
{
    entity.HasIndex(rt => rt.Token).IsUnique();
});

        builder.Entity<Badge>(entity =>
        {
            entity.HasIndex(b => b.Code).IsUnique();
            entity.HasData(
                new Badge { Id = 1, Code = "FIRST_COMPLETION", Name = "İlk adım", Description = "İlk alışkanlık kaydını oluşturdun." },
                new Badge { Id = 2, Code = "STREAK_3", Name = "3'lük seri", Description = "Aynı alışkanlıkta 3 dönem üst üste hedefi tutturdun." },
                new Badge { Id = 3, Code = "STREAK_7", Name = "Haftalık zincir", Description = "Aynı alışkanlıkta 7 dönem üst üste hedefi tutturdun." },
                new Badge { Id = 4, Code = "STREAK_30", Name = "Aylık zincir", Description = "Aynı alışkanlıkta 30 dönem üst üste hedefi tutturdun." },
                new Badge { Id = 5, Code = "READING_STREAK_7", Name = "Kitap kurdu", Description = "Okuma alışkanlığını 7 dönem üst üste sürdürdün." },
                new Badge { Id = 6, Code = "WATER_GROWTH_5", Name = "Fidancık", Description = "Su çiçeğin 5. seviyeye ulaştı." },
                new Badge { Id = 7, Code = "WATER_GROWTH_10", Name = "Çiçek bahçesi", Description = "Su çiçeğin 10. seviyeye ulaştı." }
            );
        });
        builder.Entity<TwoFactorAttempt>(entity =>
{
    entity.HasIndex(t => t.UserId).IsUnique();
});

        builder.Entity<UserBadge>(entity =>
        {
            entity.HasIndex(ub => new { ub.UserId, ub.BadgeId }).IsUnique();
            entity.HasOne(ub => ub.User)
                .WithMany(u => u.UserBadges)
                .HasForeignKey(ub => ub.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(ub => ub.Badge)
                .WithMany()
                .HasForeignKey(ub => ub.BadgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Flower>(entity =>
        {
            entity.HasIndex(f => f.UserId).IsUnique();
            entity.HasOne(f => f.User)
                .WithOne(u => u.Flower)
                .HasForeignKey<Flower>(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserNotification>(entity =>
        {
            entity.HasIndex(n => n.DedupKey).IsUnique();
            entity.HasIndex(n => new { n.UserId, n.CreatedAt });
            entity.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DeviceToken>(entity =>
        {
            entity.HasIndex(d => new { d.UserId, d.Token }).IsUnique();
            entity.HasOne(d => d.User)
                .WithMany(u => u.DeviceTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuthAuditEvent>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => new { e.Email, e.CreatedAt });
        });

        
        builder.Entity<Book>(entity =>
        {
            entity.HasIndex(b => b.UserId);
            entity.HasIndex(b => b.CreatedAt);
            entity.HasOne(b => b.User)
                .WithMany(u => u.Books)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BookReadingLog>(entity =>
        {
            entity.HasIndex(l => new { l.BookId, l.ReadDate });
            entity.HasIndex(l => l.ReadDate);
            entity.HasOne(l => l.Book)
                .WithMany(b => b.ReadingLogs)
                .HasForeignKey(l => l.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<HabitCompletion>(entity =>
        {
            entity.HasIndex(c => new { c.HabitId, c.CompletionDate });
            entity.HasIndex(c => c.CompletionDate);
        });

        builder.Entity<Pet>(entity => entity.HasIndex(p => p.CreatedAt));

        
builder.Entity<PetAccessoryUnlock>(entity =>
{
    entity.HasIndex(u => new { u.PetId, u.Accessory }).IsUnique();
    entity.HasOne(u => u.Pet)
        .WithMany(p => p.AccessoryUnlocks)
        .HasForeignKey(u => u.PetId)
        .OnDelete(DeleteBehavior.Cascade);
});

builder.Entity<UserBackgroundUnlock>(entity =>
{
    entity.HasIndex(u => new { u.UserId, u.Background }).IsUnique();
    entity.HasOne(u => u.User)
        .WithMany(u => u.BackgroundUnlocks)
        .HasForeignKey(u => u.UserId)
        .OnDelete(DeleteBehavior.Cascade);
});

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(IHasConcurrencyToken).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType)
                .Property<Guid>(nameof(IHasConcurrencyToken.ConcurrencyToken))
                .IsConcurrencyToken();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<IHasConcurrencyToken>())
        {
            if (entry.State == EntityState.Added && entry.Entity.ConcurrencyToken == Guid.Empty)
            {
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
        }
    }
}
