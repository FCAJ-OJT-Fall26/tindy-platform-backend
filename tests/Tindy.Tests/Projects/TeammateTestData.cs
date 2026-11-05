using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tindy.Application.Entities;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;

namespace Tindy.Tests.Projects;

internal static class TeammateTestData
{
    internal static async Task<ApplicationCase> Seed(TindyApiFactory factory, int required = 2, int capacity = 5)
    {
        var data = await ApplicationTestData.Seed(factory, required, capacity);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        var skill = await db.Skills.Select(s => s.SkillId).FirstAsync();
        foreach (var id in new[] { data.Leader, data.Applicant, data.Outsider })
            db.UserProfiles.Add(Profile(id, data.Role, skill));
        await db.SaveChangesAsync();
        return data;
    }

    internal static UserProfile Profile(Guid user, Guid role, Guid? skill = null) => new()
    {
        UserId = user, DisplayName = "Same display name", University = "FPT", PhoneNumber = "private-canary", Bio = "private-canary",
        PreferredRoleId = role, AvailableHoursPerWeek = 10, OpenToTeamInvitations = true, CreatedAt = DateTime.UtcNow,
        UserSkills = skill.HasValue ? [new() { UserId = user, SkillId = skill.Value }] : []
    };

    internal static ProjectInvitation Invitation(ApplicationCase data, Guid? recipient = null, DateTime? created = null)
    {
        var now = created ?? DateTime.UtcNow;
        return new()
        {
            InvitationId = Guid.NewGuid(), ProjectId = data.Project, PositionId = data.Position,
            InvitedUserId = recipient ?? data.Applicant, InvitingLeaderId = data.Leader,
            CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddHours(168)
        };
    }
    internal static string Feed(ApplicationCase data) => $"/api/projects/{data.Project}/positions/{data.Position}/candidates";

    internal static async Task<IReadOnlyList<JsonElement>> AllCandidates(HttpClient leader, string path)
    {
        var rows = new List<JsonElement>(); string? cursor = null;
        do
        {
            var separator = path.Contains('?') ? "&" : "?";
            var page = await RecruitmentHttp.Data(await leader.GetAsync(path + (cursor == null ? "" : separator + "cursor=" + Uri.EscapeDataString(cursor))));
            rows.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.Clone()));
            cursor = page.GetProperty("next_cursor").GetString();
        } while (cursor != null);
        return rows;
    }
}
