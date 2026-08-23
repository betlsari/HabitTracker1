using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Services;


public static class IdentityResultExtensions
{
    public static void EnsureSucceeded(this IdentityResult result, ILogger logger, string context, string userId)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
        logger.LogError(
            "Kullanıcı güncellemesi başarısız oldu. Context={Context} UserId={UserId} Errors={Errors}",
            context, userId, errors);

        throw new InvalidOperationException(
            $"Kullanıcı bilgileri güncellenemedi ({context}). Lütfen tekrar deneyin.");
    }
}