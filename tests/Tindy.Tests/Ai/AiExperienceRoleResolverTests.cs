using System.Text.Json.Nodes;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiExperienceRoleResolverTests
{
    private static readonly AiRoleReferenceMap Roles = new([new(Guid.NewGuid(), "AI Engineer")]);

    [Theory]
    [InlineData("AI ENGINEER", false)]
    [InlineData("Junior AI Engineer", true)]
    [InlineData("Kỹ sư AI", true)]
    [InlineData("Thực tập sinh Machine Learning", true)]
    public void AlwaysUsesExactCatalogSpellingAndPreservesSourceLevelAndLanguage(string source, bool preserveSource)
    {
        var item = new JsonObject { ["role_key"] = "r1", ["role_title_source"] = source, ["description"] = "Built models." };
        var warnings = new JsonArray();
        AiExperienceRoleResolver.Normalize(item, Roles, warnings, 0);
        Assert.Equal("AI Engineer", item["role_title"]!.GetValue<string>());
        if (preserveSource) Assert.Contains(source, item["description"]!.GetValue<string>());
        Assert.False(item.ContainsKey("role_key"));
        Assert.False(item.ContainsKey("role_title_source"));
        Assert.Empty(warnings);
    }

    [Fact]
    public void UnmatchedRoleStaysNullAndOriginalTitleIsNotLost()
    {
        var item = new JsonObject { ["role_title_source"] = "Research Scientist" };
        var warnings = new JsonArray();
        AiExperienceRoleResolver.Normalize(item, Roles, warnings, 2);
        Assert.Null(item["role_title"]);
        Assert.Equal("Research Scientist", item["description"]!.GetValue<string>());
        Assert.Equal("experiences[2].role_title", warnings[0]!["field"]!.GetValue<string>());
        Assert.Equal("UNMATCHED_CATALOG", warnings[0]!["code"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("r99", "AI Engineer")]
    [InlineData("r1", null)]
    [InlineData("r1", " ")]
    public void RejectsUnknownReferencesAndMissingSourceTitles(string key, string? source)
    {
        var item = new JsonObject { ["role_key"] = key, ["role_title_source"] = source };
        Assert.Equal(AiFailure.InvalidOutput, Assert.Throws<AiException>(() => AiExperienceRoleResolver.Normalize(item, Roles, [], 0)).Failure);
    }

    [Fact]
    public void EmptyCatalogAndUnstatedRoleRemainNull()
    {
        var item = new JsonObject();
        AiExperienceRoleResolver.Normalize(item, new([]), [], 0);
        Assert.Null(item["role_title"]);
    }
}
