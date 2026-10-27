using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiSkillEvidenceTests
{
    private static readonly Guid Sql = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Postgres = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly AiSkillReferenceMap Catalog = new([new(Sql, "SQL"), new(Postgres, "PostgreSQL")]);

    [Fact]
    public void SqlEvidenceKeepsSqlButCannotEstablishPostgreSql()
    {
        var draft = Parse(new() { ["s1"] = "SQL", ["s2"] = "SQL" });
        Assert.Equal(Sql, Assert.Single(draft.Profile.SkillIds));
        Assert.Contains(draft.Warnings, value => value.Code == "INSUFFICIENT_EVIDENCE" && value.Field == "profile.skill_ids");
    }

    [Theory]
    [InlineData("Used PostgreSQL")]
    [InlineData("Built APIs with Postgres.")]
    public void ExplicitNamesOrEquivalentNamesKeepTheCatalogId(string evidence) =>
        Assert.Equal(Postgres, Assert.Single(Parse(new() { ["s2"] = evidence }).Profile.SkillIds));

    [Fact]
    public void DuplicateJsonKeysAreRejectedBeforeDeserialization()
    {
        const string json = """{"skill_ids":{"s1":"SQL","s1":"Structured Query Language"},"experiences":[],"certificates":[],"warnings":[]}""";
        var error = Assert.Throws<AiException>(() => AiListDraftValidator.Parse(json, Catalog));
        Assert.Equal(AiFailure.InvalidOutput, error.Failure);
        Assert.Equal(json, error.RawOutput);
    }

    [Fact]
    public void UnknownKeyRemainsInvalid() =>
        Assert.Equal("unknown_skill_reference", Assert.Throws<AiException>(() => Parse(new() { ["s99"] = "SQL" })).ValidationReason);

    private static ProfileDraftResponse Parse(Dictionary<string, string> matches) => AiListDraftValidator.Parse(JsonSerializer.Serialize(new
    {
        skill_ids = matches, experiences = Array.Empty<object>(), certificates = Array.Empty<object>(), warnings = Array.Empty<object>()
    }), Catalog);
}
