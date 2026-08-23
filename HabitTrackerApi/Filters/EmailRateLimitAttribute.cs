using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Filters;


public sealed class EmailRateLimitAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _permitLimit;
    private readonly int _windowMinutes;

    public EmailRateLimitAttribute(int permitLimit = 8, int windowMinutes = 15)
    {
        _permitLimit = permitLimit;
        _windowMinutes = windowMinutes;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var email = ExtractEmail(context.ActionArguments);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var key = $"email-rl:{context.ActionDescriptor.DisplayName}:{normalizedEmail}";

            var counter = cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_windowMinutes);
                return new RateCounter();
            })!;

            var current = Interlocked.Increment(ref counter.Count);
            if (current > _permitLimit)
            {
                context.Result = new ObjectResult(
                    "Bu email adresi için çok fazla istek yapıldı. Lütfen daha sonra tekrar deneyin.")
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                };
                return;
            }
        }

        await next();
    }

    
    private static string? ExtractEmail(IDictionary<string, object?> actionArguments)
    {
        foreach (var arg in actionArguments.Values)
        {
            if (arg == null)
            {
                continue;
            }

            var property = arg.GetType().GetProperty("Email");
            if (property != null && property.PropertyType == typeof(string))
            {
                return property.GetValue(arg) as string;
            }

            var newEmailProperty = arg.GetType().GetProperty("NewEmail");
            if (newEmailProperty != null && newEmailProperty.PropertyType == typeof(string))
            {
                return newEmailProperty.GetValue(arg) as string;
            }
        }

        return null;
    }

    private sealed class RateCounter
    {
        public int Count;
    }
}