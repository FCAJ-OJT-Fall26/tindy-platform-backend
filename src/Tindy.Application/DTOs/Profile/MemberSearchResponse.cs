namespace Tindy.Application.DTOs.Profile;

// Only fields already visible on member profiles; never private contact/account data.
public sealed record MemberSearchItem(Guid UserId, string? Username, string DisplayName,
    string? University, string? Headline);
public sealed record MemberSearchResponse(IReadOnlyList<MemberSearchItem> Items, int Page, int PageSize, bool HasMore);
