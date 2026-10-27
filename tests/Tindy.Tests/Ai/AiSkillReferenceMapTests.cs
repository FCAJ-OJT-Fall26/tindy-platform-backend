using Tindy.Application.DTOs.Ai;
using Tindy.Application.Exceptions;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiSkillReferenceMapTests
{
    private static readonly AiCatalogItem Python = new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Python");
    private static readonly AiCatalogItem Torch = new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "PyTorch");

    [Fact]
    public void UsesDeterministicShortKeysWithoutExposingUuids()
    {
        var map = new AiSkillReferenceMap([Torch, Python]);
        Assert.Equal([new("s1", "Python"), new AiSkillReference("s2", "PyTorch")], map.References);
        Assert.Equal([Torch.Id, Python.Id], map.Resolve(["s2", "s1"], 30));
        Assert.Equal(map.References, new AiSkillReferenceMap([Python, Torch]).References);
    }

    [Fact]
    public void RequestMapsRemainIndependentAndSnapshotTheirInput()
    {
        var catalog = new List<AiCatalogItem> { Python };
        var first = new AiSkillReferenceMap(catalog);
        catalog.Clear();
        var second = new AiSkillReferenceMap([Torch]);
        Assert.Equal(Python.Id, Assert.Single(first.Resolve(["s1"], 30)));
        Assert.Equal(Torch.Id, Assert.Single(second.Resolve(["s1"], 30)));
        Assert.Throws<AiException>(() => first.Resolve(["s2"], 30));
    }

    [Theory]
    [InlineData("S1")]
    [InlineData("s0")]
    [InlineData("s01")]
    [InlineData("unknown")]
    [InlineData("11111111-1111-4111-8111-111111111111")]
    public void RejectsUnknownOrInventedKeys(string key) =>
        Assert.Throws<AiException>(() => new AiSkillReferenceMap([Python]).Resolve([key], 30));

    [Fact]
    public void RejectsNullDuplicateAndExcessReferences()
    {
        var map = new AiSkillReferenceMap([Python, Torch]);
        Assert.Throws<AiException>(() => map.Resolve(null, 30));
        Assert.Throws<AiException>(() => map.Resolve([null!], 30));
        Assert.Throws<AiException>(() => map.Resolve(["s1", "s1"], 30));
        Assert.Throws<AiException>(() => map.Resolve(["s1", "s2"], 1));
        Assert.Empty(map.Resolve([], 30));
        Assert.Empty(new AiSkillReferenceMap([]).References);
    }
}
