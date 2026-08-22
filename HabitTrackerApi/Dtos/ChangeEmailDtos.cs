using System.ComponentModel.DataAnnotations;

namespace Dtos;

public class RequestEmailChangeDto
{
    [Required, EmailAddress]
    public string NewEmail { get; set; } = string.Empty;
}

public sealed class ConfirmEmailChangeDto : RequestEmailChangeDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
}
