using System.Text.Json.Serialization;

namespace Tindy.Application.DTOs.Ai;

public static class AiProfileLimits
{
    public const int MaxPdfBytes = 4 * 1024 * 1024;
    public const int MaxRequestBytes = 5 * 1024 * 1024;
    public const int MaxTextLength = 20_000;
    public const int MaxCollectionItems = 20;
    public const int MaxWarnings = 50;
    public const int MaxOutputBytes = 128 * 1024;
}

public sealed record AiProfileInput(string? Text, byte[]? Pdf);
public sealed record AiCatalogItem(Guid Id, string Name);

// Suggestions deliberately allow missing fields required by the existing save APIs.
// They are never entities or commands to write a profile.
public sealed record ProfileDraftResponse
{
    [JsonIgnore] public AiCallDiagnostics? Diagnostics { get; init; }
    [JsonRequired] public ProfileDraft Profile { get; init; } = new();
    [JsonRequired] public IReadOnlyList<ExperienceDraft> Experiences { get; init; } = [];
    [JsonRequired] public IReadOnlyList<CertificateDraft> Certificates { get; init; } = [];
    [JsonRequired] public IReadOnlyList<ProfileDraftWarning> Warnings { get; init; } = [];
}

public sealed record ProfileDraft
{
    public string? DisplayName { get; init; }
    public string? University { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Headline { get; init; }
    public string? Location { get; init; }
    public string? CurrentRole { get; init; }
    public Guid? PreferredRoleId { get; init; }
    public int? AvailableHoursPerWeek { get; init; }
    public string? ProfessionalInterests { get; init; }
    public string? LearningGoals { get; init; }
    public string? PreviousProjectsAndContribution { get; init; }
    public string? TechnologiesToLearn { get; init; }
    public string? GithubUrl { get; init; }
    public string? LinkedinUrl { get; init; }
    public string? PersonalWebsiteUrl { get; init; }
    public string? PublicEmail { get; init; }
    public IReadOnlyList<Guid> SkillIds { get; init; } = [];
}

public sealed record ExperienceDraft
{
    public string? ProjectName { get; init; }
    public string? RoleTitle { get; init; }
    public string? Description { get; init; }
    public string? RepoUrl { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public bool? IsOngoing { get; init; }
    public IReadOnlyList<Guid> SkillIds { get; init; } = [];
}

public sealed record CertificateDraft
{
    public string? Title { get; init; }
    public string? Issuer { get; init; }
    public string? CredentialId { get; init; }
    public string? CredentialUrl { get; init; }
    public string? IssuedAt { get; init; }
    public string? ExpiresAt { get; init; }
}

public sealed record ProfileDraftWarning(string Field, string Code, string Message);
