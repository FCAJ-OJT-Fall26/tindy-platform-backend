using System.ComponentModel.DataAnnotations;

namespace Tindy.Application.DTOs.Authentication;

public sealed class OAuthExchangeRequest
{
    [Required]
    public string Ticket { get; init; } = string.Empty;
}