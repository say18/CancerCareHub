
using System.ComponentModel.DataAnnotations;

namespace CancerCareHub.Models;

public class VerifyOtpViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{6}$",
        ErrorMessage = "OTP must contain exactly 6 digits.")]
    public string Code { get; set; } = string.Empty;
}
