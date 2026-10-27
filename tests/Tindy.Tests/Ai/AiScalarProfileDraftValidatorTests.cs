using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiScalarProfileDraftValidatorTests
{
    private static readonly Guid RoleId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly AiRoleReferenceMap Roles = new([new(RoleId, "AI Engineer")]);

    [Fact]
    public void ExtractsNullableScalarFieldsWithoutChangingThePublicShape()
    {
        var result = AiScalarProfileDraftValidator.Parse("""{"profile":{"display_name":"Ngọc","current_role":"AI Engineer","public_email":"person@example.com","available_hours_per_week":12},"warnings":[]}""", Roles);
        Assert.Equal("Ngọc", result.Profile.DisplayName);
        Assert.Equal(12, result.Profile.AvailableHoursPerWeek);
        Assert.Null(result.Profile.PreferredRoleId);
        Assert.Null(result.Profile.University);
        Assert.Empty(result.Profile.SkillIds);
        Assert.Empty(result.Experiences);
        Assert.Empty(result.Certificates);
    }

    [Theory]
    [InlineData("I want to work as an AI Engineer.", true)]
    [InlineData("Tôi mong muốn làm AI Engineer.", true)]
    [InlineData("AI Engineer", false)]
    [InlineData("I am an AI Engineer. I want to work in a technical environment.", false)]
    [InlineData("Tôi mong muốn được làm việc trong môi trường kỹ thuật cao, đề cao tư duy logic, học hỏi liên tục và tinh thần hợp tác để đóng góp lâu dài vào chiến lược AI của doanh nghiệp.", false)]
    [InlineData("I do not want to be an AI Engineer.", false)]
    [InlineData(null, false)]
    public void RequiresAnAffirmativePreferenceRatherThanCurrentRole(string? statement, bool accepted)
    {
        var json = JsonSerializer.Serialize(new { profile = new { current_role = "AI Engineer", preferred_role_key = "r1", preferred_role_statement = statement }, warnings = Array.Empty<object>() });
        var result = AiScalarProfileDraftValidator.Parse(json, Roles);
        Assert.Equal(accepted ? RoleId : (Guid?)null, result.Profile.PreferredRoleId);
        if (!accepted) Assert.Contains(result.Warnings, warning => warning.Code == "INSUFFICIENT_EVIDENCE");
    }

    [Theory]
    [InlineData("{\"profile\":{\"bio\":\"invented\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"skill_ids\":[]},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"preferred_role_id\":null},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"preferred_role_key\":\"r99\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"available_hours_per_week\":41},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"available_hours_per_week\":\"12\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"github_url\":\"javascript:alert(1)\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"public_email\":\"not-email\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{\"display_name\":\"A\",\"display_name\":\"B\"},\"warnings\":[]}")]
    [InlineData("{\"profile\":{},\"warnings\":[],\"experiences\":[]}")]
    [InlineData("not json")]
    public void RejectsInvalidOutputAndRetainsRawDiagnostics(string json)
    {
        var error = Assert.Throws<AiException>(() => AiScalarProfileDraftValidator.Parse(json, Roles));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(json, error.RawOutput);
        Assert.DoesNotContain(json, error.ToString());
    }

    [Fact]
    public void EmptyProfileAndRoleCatalogRemainValidWithWarnings()
    {
        var draft = AiScalarProfileDraftValidator.Parse("""{"profile":{},"warnings":[]}""", new([]));
        Assert.Null(draft.Profile.DisplayName);
        Assert.Null(draft.Profile.PreferredRoleId);
        Assert.Contains(draft.Warnings, warning => warning.Code == "INSUFFICIENT_EVIDENCE");
    }

    [Theory]
    [InlineData("university", false)]
    [InlineData("learning_goals", false)]
    [InlineData("profile.university", true)]
    [InlineData("profile.learning_goals", true)]
    public void WarningPathsRemainStrictAndCannotBypassRoleEvidence(string path, bool valid)
    {
        var json = JsonSerializer.Serialize(new
        {
            profile = new { display_name = "Lan", current_role = "AI Engineer", preferred_role_key = "r1",
                preferred_role_statement = "Tôi mong muốn được làm việc trong môi trường kỹ thuật cao." },
            warnings = new[] { new { field = path, code = "MISSING_INFORMATION", message = "Not stated." } }
        });
        if (!valid)
        {
            var error = Assert.Throws<AiException>(() => AiScalarProfileDraftValidator.Parse(json, Roles));
            Assert.Equal(AiFailure.InvalidOutput, error.Failure);
            Assert.Equal(json, error.RawOutput);
            return;
        }
        var result = AiScalarProfileDraftValidator.Parse(json, Roles);
        Assert.Null(result.Profile.PreferredRoleId);
        Assert.Contains(result.Warnings, warning => warning.Field == "profile.preferred_role_id" && warning.Code == "INSUFFICIENT_EVIDENCE");
    }
}
