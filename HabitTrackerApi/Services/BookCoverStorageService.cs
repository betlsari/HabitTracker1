using Data;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// Kitap kapak görsellerinin diskteki fiziksel dosyalarını yönetir.
/// EF Core cascade delete sadece DB satırlarını siler; kapak dosyaları
/// diskte kalıp öksüzleşir. Bu servis, kapak değiştirildiğinde veya
/// kullanıcı hesabı silindiğinde ilgili dosyaların da temizlenmesini sağlar.
/// </summary>
public class BookCoverStorageService
{
    private const string RelativeCoverPrefix = "/uploads/covers/";

    private readonly IWebHostEnvironment _environment;
    private readonly AppDbContext _context;

    public BookCoverStorageService(IWebHostEnvironment environment, AppDbContext context)
    {
        _environment = environment;
        _context = context;
    }

    public void DeleteCoverFile(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl) ||
            !coverUrl.StartsWith(RelativeCoverPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var fileName = Path.GetFileName(coverUrl);
        if (fileName != coverUrl[RelativeCoverPrefix.Length..])
        {
            return;
        }

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "covers", fileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

   
    public async Task DeleteAllCoversForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var coverUrls = await _context.Books
            .Where(b => b.UserId == userId && b.CoverImageUrl != null)
            .Select(b => b.CoverImageUrl!)
            .ToListAsync(cancellationToken);

        foreach (var url in coverUrls)
        {
            DeleteCoverFile(url);
        }
    }
}