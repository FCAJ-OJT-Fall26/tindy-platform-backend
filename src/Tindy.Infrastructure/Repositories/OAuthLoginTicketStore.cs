using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;
using Tindy.Infrastructure.Persistence.Models;

namespace Tindy.Infrastructure.Repositories;

/// <summary>
/// Shared PostgreSQL store. A conditional DELETE ensures one successful exchange
/// even when two API instances receive the same ticket concurrently.
/// </summary>
public sealed class OAuthLoginTicketStore : IOAuthLoginTicketStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(90);
    private readonly TindyDbContext _db;

    public OAuthLoginTicketStore(TindyDbContext db) => _db = db;

    public async Task<string> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Invalid user ID", nameof(userId));

        // Housekeeping: bounded by time-based index; also safe on multiple instances.
        var now = DateTime.UtcNow;
        await _db.OAuthLoginTickets.Where(x => x.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var secret = RandomNumberGenerator.GetBytes(32);
        var ticket = Convert.ToBase64String(secret).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(secret));
        await _db.OAuthLoginTickets.AddAsync(new OAuthLoginTicket
        {
            TicketHash = hash,
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime)
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return ticket;
    }

    public async Task<Guid?> ConsumeAsync(string ticket, CancellationToken cancellationToken = default)
    {
        // 32 random bytes encoded as unpadded Base64Url are always 43 characters.
        if (string.IsNullOrWhiteSpace(ticket) || ticket.Length != 43 ||
            ticket.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return null;

        byte[] secret;
        try
        {
            var base64 = ticket.Replace('-', '+').Replace('_', '/');
            secret = Convert.FromBase64String(base64 + "=");
        }
        catch (FormatException) { return null; }
        if (secret.Length != 32) return null;

        var hash = Convert.ToHexString(SHA256.HashData(secret));
        var now = DateTime.UtcNow;
        var candidate = await _db.OAuthLoginTickets.AsNoTracking()
            .Where(x => x.TicketHash == hash && x.ExpiresAt > now)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!candidate.HasValue) return null;

        // At most one caller can delete the row. No JWT is issued to any other caller.
        var deleted = await _db.OAuthLoginTickets
            .Where(x => x.TicketHash == hash && x.ExpiresAt > now)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1 ? candidate : null;
    }
}
