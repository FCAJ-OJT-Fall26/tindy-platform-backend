using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Tindy.Application.Interfaces.Services;
using Tindy.Tests.Integration;
using Xunit;

namespace Tindy.Tests.Projects;

public sealed class ChatLiveBrowserFactAttribute : FactAttribute
{
    public ChatLiveBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TINDY_CHAT_BROWSER") != "1")
            Skip = "Set TINDY_CHAT_BROWSER=1 to run Chrome against isolated Kestrel/PostgreSQL.";
    }
}

public sealed class ChatBrowserTests
{
    [ChatLiveBrowserFact]
    public async Task TwoRealBrowsers_RequestConsentMessageAndBlock()
    {
        var live = new TindyApiFactory();
        live.UseKestrel(0);
        try
        {
            await live.InitializeAsync();
            var data = await TeammateTestData.Seed(live);
            // Rich disposable profiles for visual verification of the candidate dialog.
            using (var visualScope = live.Services.CreateScope())
            {
                var db = visualScope.ServiceProvider.GetRequiredService<Tindy.Infrastructure.Persistence.TindyDbContext>();
                foreach (var user in new[] { data.Applicant, data.Outsider })
                {
                    var profile = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.UserProfiles, p => p.UserId == user);
                    profile.DisplayName = user == data.Applicant ? "Jordan Nguyen" : "Alex Morgan";
                    profile.Headline = "Building thoughtful products with curious teams";
                    profile.Bio = "I enjoy turning complex problems into simple, reliable experiences. Looking to collaborate with a team that cares about quality, thoughtful design, and learning along the way.";
                    foreach (var project in new[] { "Community Platform", "Team Delivery Workshop" })
                        db.ProfileExperiences.Add(new Tindy.Application.Entities.ProfileExperience { ExperienceId = Guid.NewGuid(), UserId = user, ProjectName = project, RoleTitle = "Product Engineer", Description = "Worked with a small team to shape requirements, build product features, and improve quality through automated testing and regular peer reviews.", StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 8, 1), CreatedAt = DateTime.UtcNow });
                    db.UserCertificates.Add(new Tindy.Application.Entities.UserCertificate { CertificateId = Guid.NewGuid(), UserId = user, Title = "Software Quality Foundations", Issuer = "Tindy Learning", Description = "Practical foundations in test planning, automation, and collaborative delivery.", CreatedAt = DateTime.UtcNow });
                }
                await db.SaveChangesAsync();
            }
            var address = live.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var scope = live.Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "tindy-platform-frontend"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(directory.FullName, "tindy-platform-frontend"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("tests/chat-requests.live.browser.mjs");
            start.Environment["TINDY_CHAT_API"] = address;
            start.Environment["TINDY_CHAT_LEADER_TOKEN"] = tokens.GenerateAccessToken(data.Leader, ["Member"]).AccessToken;
            start.Environment["TINDY_CHAT_MEMBER_TOKEN"] = tokens.GenerateAccessToken(data.Applicant, ["Member"]).AccessToken;
            start.Environment["TINDY_CHAT_PROJECT"] = data.Project.ToString();
            start.Environment["TINDY_CHAT_MEMBER"] = data.Applicant.ToString();
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + "\n" + await errors);
        }
        finally { await ((IAsyncLifetime)live).DisposeAsync(); }
    }
}
