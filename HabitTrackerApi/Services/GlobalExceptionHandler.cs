using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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
        // DÜZELTİLDİ (🟠 tutarsızlık): Program.cs'de builder.Services.AddProblemDetails()
        // kayıtlı olmasına rağmen önceden özel bir anonim JSON şeması
        // ({ error, statusCode }) dönülüyordu. Bu, framework'ün üretim tarzı
        // (validation hataları, 404 vb. için) ürettiği standart RFC7807
        // ProblemDetails ({type, title, status, detail, instance}) ile
        // tutarsızdı. Artık bu handler da aynı standart şemayı kullanıyor.
        var (statusCode, title, detail) = exception switch
        {
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Eşzamanlılık çakışması",
                "Bu kayıt sizden önce başka bir işlem tarafından değiştirilmiş/silinmiş. Lütfen sayfayı yenileyip tekrar deneyin."),

            DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx) => (
                StatusCodes.Status409Conflict,
                "Kayıt zaten mevcut",
                "Bu kayıt zaten mevcut."),

            DbUpdateException => (
                StatusCodes.Status400BadRequest,
                "Geçersiz istek",
                "Veri kaydedilirken bir hata oluştu. Girdiğiniz verileri kontrol edip tekrar deneyin."),

            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "İstek iptal edildi",
                "İstek iptal edildi."),

            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "Zaman aşımı",
                "İşlem zaman aşımına uğradı. Lütfen tekrar deneyin."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Beklenmedik bir hata oluştu",
                "Beklenmedik bir hata oluştu.")
        };

        var logLevel = statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(logLevel, exception, "İstek işlenirken hata oluştu. StatusCode={StatusCode}", statusCode);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = context.Request.Path
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
    }
}