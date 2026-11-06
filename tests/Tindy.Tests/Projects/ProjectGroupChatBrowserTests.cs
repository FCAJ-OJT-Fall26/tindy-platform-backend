using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Interfaces.Services;
using Tindy.Infrastructure.Persistence;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ProjectGroupChatBrowserTests
{
    [ChatLiveBrowserFact]
    public async Task RealBrowsers_ProjectLinkRealtimeDepartureRejoinAndMobile()
    {
        var live = new TindyApiFactory(); live.UseKestrel(0);
        try
        {
            await live.InitializeAsync();
            var data = await TeammateTestData.Seed(live);
            using var scope = live.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TindyDbContext>();
            await db.UserProfiles.Where(p => p.UserId == data.Leader).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "Project Leader"));
            await db.UserProfiles.Where(p => p.UserId == data.Applicant).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "Jordan Nguyen"));
            var address = live.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "tindy-platform-frontend"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(directory.FullName, "tindy-platform-frontend"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("tests/project-group-chat.live.browser.mjs");
            start.Environment["TINDY_CHAT_API"] = address;
            start.Environment["TINDY_CHAT_LEADER_TOKEN"] = tokens.GenerateAccessToken(data.Leader, ["Member"]).AccessToken;
            start.Environment["TINDY_CHAT_MEMBER_TOKEN"] = tokens.GenerateAccessToken(data.Applicant, ["Member"]).AccessToken;
            start.Environment["TINDY_CHAT_PROJECT"] = data.Project.ToString();
            start.Environment["TINDY_CHAT_MEMBER"] = data.Applicant.ToString();
            start.Environment["TINDY_CHAT_POSITION"] = data.Position.ToString();
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + "\n" + await errors);
        }
        finally { await ((IAsyncLifetime)live).DisposeAsync(); }
    }
}
