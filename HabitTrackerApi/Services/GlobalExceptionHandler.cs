using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Services;
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,CancellationToken cancellationToken)
    {
       _logger.LogError(exception, "Beklenmedik bir hata oluştu.");

context.Response.StatusCode = StatusCodes.Status500InternalServerError;
context.Response.ContentType = "application/json";

var response = new
{
    error = "Beklenmedik bir hata oluştu.",
    statusCode = 500
};

await context.Response.WriteAsJsonAsync(response, cancellationToken);

return true;
    }




}