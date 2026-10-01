using System.ComponentModel.DataAnnotations;

namespace SaasPOS.Application.DTOs;

public class PasswordRecoveryRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
