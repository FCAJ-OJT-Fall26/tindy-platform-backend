using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiRoleReferenceMapTests
{
    private static readonly AiCatalogItem Backend = new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Backend Developer");
    private static readonly AiCatalogItem Ai = new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "AI Engineer");

    [Fact]
    public void UsesSortedImmutableRequestLocalRoleKeys()
    {
        var source = new List<AiCatalogItem> { Ai, Backend };
        var map = new AiRoleReferenceMap(source);
        source.Clear();
        Assert.Equal(Backend.Id, map.Resolve("r1"));
        Assert.Equal(Ai.Id, map.Resolve("r2"));
        Assert.Null(map.Resolve(null));
        Assert.Equal("Backend Developer", map.References[0].Name);
        Assert.Equal(Ai.Id, new AiRoleReferenceMap([Ai]).Resolve("r1"));
        Assert.Equal(Backend.Id, map.Resolve("r1"));
    }

    [Theory]
    [InlineData("r0")]
    [InlineData("R1")]
    [InlineData("r01")]
    [InlineData("s1")]
    [InlineData("11111111-1111-4111-8111-111111111111")]
    public void RejectsUnknownRoleKeys(string key) =>
        Assert.Equal("unknown_role_reference", Assert.Throws<AiException>(() => new AiRoleReferenceMap([Backend]).Resolve(key)).ValidationReason);

    [Fact]
    public void SupportsEmptyCatalogAndRejectsDuplicateCatalogIds()
    {
        var map = new AiRoleReferenceMap([]);
        Assert.Empty(map.References);
        Assert.Empty(map.ActiveIds);
        Assert.Null(map.Resolve(null));
        Assert.Throws<AiException>(() => map.Resolve("r1"));
        Assert.Throws<ArgumentException>(() => new AiRoleReferenceMap([Ai, Ai]));
    }
}
