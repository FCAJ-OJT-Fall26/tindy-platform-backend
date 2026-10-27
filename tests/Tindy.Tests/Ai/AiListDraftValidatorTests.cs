using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiListDraftValidatorTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly AiSkillReferenceMap Map = new([new(Id, "Python")]);

    [Fact]
    public void MapsProfileAndExperienceSkillsAndPreservesPublicShape()
    {
        var output = AiListDraftValidator.Parse("""
        {"skill_ids":["s1"],"experiences":[{"role_title":"AI Engineer","description":"Used Python","start_date":"2021-01","end_date":null,"is_ongoing":true,"skill_ids":["s1"]}],
        "certificates":[{"title":"ML Certificate","issued_at":null}],
        "warnings":[{"field":"skill_ids","code":"UNMATCHED_CATALOG","message":"SQL has no exact match."}]}
        """, Map);
        Assert.Equal(Id, Assert.Single(output.Profile.SkillIds));
        Assert.Equal(Id, Assert.Single(Assert.Single(output.Experiences).SkillIds));
        Assert.Null(output.Profile.DisplayName);
        Assert.Null(output.Profile.PreferredRoleId);
        Assert.Null(Assert.Single(output.Certificates).IssuedAt);
        Assert.Equal("profile.skill_ids", Assert.Single(output.Warnings).Field);
    }

    [Theory]
    [InlineData("{\"skill_ids\":[\"s1\",\"s1\"],\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[\"s999\"],\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[null],\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[1],\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[],\"experiences\":[{\"skill_ids\":[]}],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[],\"experiences\":[],\"certificates\":[{\"title\":\"Cert\",\"issued_at\":\"2021\"}],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[],\"experiences\":[],\"certificates\":[],\"warnings\":[],\"profile\":{}}")]
    [InlineData("{\"skill_ids\":[],\"skill_ids\":[],\"experiences\":[],\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[],\"experiences\":null,\"certificates\":[],\"warnings\":[]}")]
    [InlineData("{\"skill_ids\":[]")]
    public void RejectsInvalidListsAndKeepsOriginalRawOutput(string json)
    {
        var error = Assert.Throws<AiException>(() => AiListDraftValidator.Parse(json, Map));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(json, error.RawOutput);
    }

    [Fact]
    public void EnforcesThirtyDistinctSkillsWithoutSilentlyTrimming()
    {
        var map = new AiSkillReferenceMap(Enumerable.Range(0, 31).Select(i => new AiCatalogItem(Guid.NewGuid(), $"Skill {i}")));
        var json = JsonSerializer.Serialize(new { skill_ids = map.References.Select(x => x.Key), experiences = Array.Empty<object>(), certificates = Array.Empty<object>(), warnings = Array.Empty<object>() });
        Assert.Throws<AiException>(() => AiListDraftValidator.Parse(json, map));
    }

    [Fact]
    public void EmptyEvidenceProducesAnInsufficientEvidenceWarning()
    {
        var draft = AiListDraftValidator.Parse("""{"skill_ids":[],"experiences":[],"certificates":[],"warnings":[]}""", Map);
        Assert.Empty(draft.Profile.SkillIds);
        Assert.Contains(draft.Warnings, warning => warning.Code == "INSUFFICIENT_EVIDENCE");
    }
}
