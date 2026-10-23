using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Tindy.Application.Exceptions;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Services;
using Tindy.Infrastructure.Storage;
using SkiaSharp;
using Xunit;

namespace Tindy.Tests.Integration;

public sealed class ProfileMediaServiceIntegrationTests
{
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task Upload_Replace_Delete_ArePrivateAndLeaveOldAssetForCleanup()
    {
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("tindy_profile_media_test")
            .WithUsername("postgres")
            .WithPassword("postgres_test_only")
            .Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<TindyDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new TindyDbContext(options);
        await db.Database.MigrateAsync();

        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO users (user_id, status, is_onboarding_completed, created_at) VALUES ({owner}, {"active"}, {true}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO user_profiles (user_id, display_name, created_at) VALUES ({owner}, {"Test Owner"}, {now})");

        var root = Path.Combine(Path.GetTempPath(), "tindy-step03-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalFileStorageService(root, Directory.GetCurrentDirectory());
            var service = new ProfileMediaService(db, storage, new TestHttpClientFactory());
            using var bitmap = new SKBitmap(640, 480);
            bitmap.Erase(SKColors.CornflowerBlue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 95);
            var bytes = encoded.ToArray();

            async Task<Guid> Upload()
            {
                await using var input = new MemoryStream(bytes);
                return (await service.UploadAsync(owner, true, input, "avatar.png", bytes.Length, CancellationToken.None))
                    .AvatarMediaAssetId!.Value;
            }

            var first = await Upload();
            await using (var current = (await service.ReadAsync(first, CancellationToken.None)).Handle)
            {
                Assert.True(current.Content.CanRead);
                Assert.True(current.Content.Length > 0);
            }
            var second = await Upload();
            Assert.NotEqual(first, second);
            Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking()
                .SingleAsync(a => a.MediaAssetId == first)).Status);
            var firstNotFound = await Assert.ThrowsAsync<MediaRequestException>(() =>
                service.ReadAsync(first, CancellationToken.None));
            Assert.Equal(404, firstNotFound.StatusCode);

            await service.RemoveAsync(owner, true, CancellationToken.None);
            Assert.Equal("pending_delete", (await db.MediaAssets.AsNoTracking()
                .SingleAsync(a => a.MediaAssetId == second)).Status);
            var profile = await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == owner);
            Assert.Null(profile.AvatarAssetId);
            Assert.Equal("default", profile.AvatarSource);
            Assert.Null(profile.AvatarUrl);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
