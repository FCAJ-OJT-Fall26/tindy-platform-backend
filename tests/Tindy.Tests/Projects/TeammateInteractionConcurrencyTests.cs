using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class TeammateInteractionConcurrencyTests(TindyApiFactory factory) : IClassFixture<TindyApiFactory>
{
    [Fact]
    public async Task IndependentRequests_UpsertAndDelete_SerializeWithoutDuplicatesOrServerErrors()
    {
        var data = await TeammateTestData.Seed(factory);
        using var leader = RecruitmentHttp.Client(factory, data.Leader);
        var path = TeammateInteractionHttpTests.Path(data, data.Applicant);
        var first = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => leader.PutAsJsonAsync(path, new { action = "saved" })));
        Assert.All(first, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
        Assert.Equal(1, await db.TeammateDiscoveryInteractions.CountAsync(i => i.ProjectId == data.Project));
        for (var i = 0; i < 5; i++)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<HttpResponseMessage> Run(Func<Task<HttpResponseMessage>> action) { await gate.Task; return await action(); }
            var put = Run(() => leader.PutAsJsonAsync(path, new { action = i % 2 == 0 ? "shortlisted" : "skipped" }));
            var delete = Run(() => leader.DeleteAsync(path));
            gate.SetResult();
            Assert.All(await Task.WhenAll(put, delete), response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.InRange(await db.TeammateDiscoveryInteractions.CountAsync(x => x.ProjectId == data.Project), 0, 1);
        }
    }
}
