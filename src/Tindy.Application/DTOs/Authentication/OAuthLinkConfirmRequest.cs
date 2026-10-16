using System.ComponentModel.DataAnnotations;

namespace Tindy.Application.DTOs.Authentication;

public sealed class OAuthLinkConfirmRequest
{
    [Required]
    [MaxLength(256)]
    public string LinkTicket { get; init; } = string.Empty;
}
