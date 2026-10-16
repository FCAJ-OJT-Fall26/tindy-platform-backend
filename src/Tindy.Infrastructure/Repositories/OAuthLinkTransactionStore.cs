using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.Constants;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Application.Models.Authentication;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Models;

namespace Tindy.Infrastructure.Repositories;

/// <summary>PostgreSQL-backed one-time tokens; never persist raw link tickets.</summary>
public sealed class OAuthLinkTransactionStore : IOAuthLinkTransactionStore
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ConfirmLifetime = TimeSpan.FromSeconds(90);
    private readonly TindyDbContext _db;

    public OAuthLinkTransactionStore(TindyDbContext db) => _db = db;

    public async Task<string> IssueStartAsync(Guid userId, string provider,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await _db.OAuthLinkTransactions.Where(x => x.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
        var token = MakeToken();
        await _db.OAuthLinkTransactions.AddAsync(new OAuthLinkTransaction
        {
            TransactionId = Guid.NewGuid(),
            StartTicketHash = Hash(token),
            UserId = userId,
            Provider = provider,
            Status = "pending",
            CreatedAt = now,
            ExpiresAt = now.Add(ChallengeLifetime)
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<Guid?> ConsumeStartAsync(string startToken, string provider,
        CancellationToken cancellationToken = default)
    {
        if (!TryHash(startToken, out var hash)) return null;
        var now = DateTime.UtcNow;
        var count = await _db.OAuthLinkTransactions.Where(x => x.StartTicketHash == hash &&
            x.Provider == provider && x.Status == "pending" && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "challenged"), cancellationToken);
        if (count != 1) return null;
        return await _db.OAuthLinkTransactions.AsNoTracking()
            .Where(x => x.StartTicketHash == hash)
            .Select(x => (Guid?)x.TransactionId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> CompleteProviderAsync(Guid transactionId,
        AuthenticatedExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        if (identity is null || identity.ProviderUserId is null) return null;
        var provider = ExternalAuthProviders.Normalize(identity.Provider);
        var providerUserId = identity.ProviderUserId.Trim();
        if (providerUserId.Length is 0 or > 255) return null;
        var ticket = MakeToken();
        var completionHash = Hash(ticket);
        var providerUsername = Crop(identity.ProviderUsername, 255);
        var displayName = Crop(identity.DisplayName, 100);
        var email = Crop(identity.Email, 255);
        var verified = identity.EmailVerified;
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ConfirmLifetime);
        var count = await _db.OAuthLinkTransactions.Where(x => x.TransactionId == transactionId &&
                x.Provider == provider && x.Status == "challenged" && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "verified")
                .SetProperty(x => x.ProviderUserId, providerUserId)
                .SetProperty(x => x.ProviderUsername, providerUsername)
                .SetProperty(x => x.DisplayName, displayName)
                .SetProperty(x => x.ProviderEmail, email)
                .SetProperty(x => x.EmailVerified, verified)
                .SetProperty(x => x.CompletionTicketHash, completionHash)
                .SetProperty(x => x.ExpiresAt, expiresAt), cancellationToken);
        return count == 1 ? ticket : null;
    }

    public async Task<AuthenticatedExternalIdentity?> ConsumeCompletionAsync(string linkTicket, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!TryHash(linkTicket, out var hash)) return null;
        var now = DateTime.UtcNow;
        var row = await _db.OAuthLinkTransactions.AsNoTracking()
            .Where(x => x.CompletionTicketHash == hash && x.UserId == userId &&
                        x.Status == "verified" && x.ExpiresAt > now && x.ProviderUserId != null)
            .Select(x => new { x.Provider, x.ProviderUserId, x.ProviderUsername,
                x.DisplayName, x.ProviderEmail, x.EmailVerified })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var deleted = await _db.OAuthLinkTransactions.Where(x => x.CompletionTicketHash == hash &&
                x.UserId == userId && x.Status == "verified" && x.ExpiresAt > now)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? new AuthenticatedExternalIdentity(row.Provider, row.ProviderUserId!,
            row.DisplayName, row.ProviderUsername, row.ProviderEmail, row.EmailVerified) : null;
    }

    private static string? Crop(string? value, int maxLength) => value is null
        ? null : value[..Math.Min(maxLength, value.Length)];

    private static string MakeToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');
        var raw = Convert.FromBase64String(base64 + "=");
        return Convert.ToHexString(SHA256.HashData(raw));
    }

    private static bool TryHash(string? token, out string hash)
    {
        hash = string.Empty;
        if (token?.Length != 43 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;
        try
        {
            var base64 = token.Replace('-', '+').Replace('_', '/');
            var raw = Convert.FromBase64String(base64 + "=");
            if (raw.Length != 32) return false;
            hash = Convert.ToHexString(SHA256.HashData(raw));
            return true;
        }
        catch (FormatException) { return false; }
    }
}
