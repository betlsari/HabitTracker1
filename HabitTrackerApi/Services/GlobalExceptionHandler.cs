using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // DÜZELTİLDİ: Önceden her exception aynı şekilde (500 + jenerik mesaj)
        // işleniyordu. Artık en yaygın/anlamlı EF Core hata türleri ayrıştırılıp
        // daha doğru bir HTTP status kodu ve mesajla dönüyor; istemci (özellikle
        // eşzamanlı düzenleme çakışmalarında) buna göre davranabiliyor.
        var (statusCode, error) = exception switch
        {
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Bu kayıt sizden önce başka bir işlem tarafından değiştirilmiş/silinmiş. Lütfen sayfayı yenileyip tekrar deneyin."),

            DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx) => (
                StatusCodes.Status409Conflict,
                "Bu kayıt zaten mevcut."),

            DbUpdateException => (
                StatusCodes.Status400BadRequest,
                "Veri kaydedilirken bir hata oluştu. Girdiğiniz verileri kontrol edip tekrar deneyin."),

            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "İstek iptal edildi."),

            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "İşlem zaman aşımına uğradı. Lütfen tekrar deneyin."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Beklenmedik bir hata oluştu.")
        };

        var logLevel = statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(logLevel, exception, "İstek işlenirken hata oluştu. StatusCode={StatusCode}", statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error,
            statusCode
        };

        await context.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        
        return ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
    }
}