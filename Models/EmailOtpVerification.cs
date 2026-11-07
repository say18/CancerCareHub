
using System.ComponentModel.DataAnnotations;

namespace CancerCareHub.Models;

public class EmailOtpVerification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public int Attempts { get; set; }

    public bool IsUsed { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
