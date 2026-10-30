using System.Security.Cryptography;
using System.Text;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;

namespace Tindy.Application.Services;

// Bounded, process-local snapshots. A restart/eviction returns a refreshable 400,
// never silently changes pagination order. No persistence of pairwise scores.
public sealed class DiscoveryFeedSessions(TimeProvider clock)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly object _gate = new();
    public sealed record Session(Guid Id, Guid UserId, DateTime Snapshot, DateTime ExpiresAt,
        int Limit, DiscoveryFilters Filters, IReadOnlyList<DiscoveryCard> Cards);

    public Session Create(Guid userId, DateTime snapshot, int limit, DiscoveryFilters filters, IReadOnlyList<DiscoveryCard> cards)
    {
        lock (_gate)
        {
            foreach (var key in _sessions.Where(s => s.Value.ExpiresAt <= clock.GetUtcNow().UtcDateTime).Select(s => s.Key).ToArray())
                _sessions.Remove(key);
            foreach (var key in _sessions.Values.Where(s => s.UserId == userId).OrderByDescending(s => s.Snapshot).Skip(3).Select(s => s.Id).ToArray())
                _sessions.Remove(key);
            while (_sessions.Count > 0 && (_sessions.Count >= 128 || _sessions.Values.Sum(s => s.Cards.Count) + cards.Count > 100_000))
                _sessions.Remove(_sessions.Values.OrderBy(s => s.Snapshot).First().Id);
            var session = new Session(Guid.NewGuid(), userId, snapshot, snapshot.AddMinutes(15), limit, filters, cards);
            if (cards.Count <= 100_000) _sessions.Add(session.Id, session);
            else throw new ProjectException(503, "DISCOVERY_SNAPSHOT_TOO_LARGE", "Discovery snapshot is too large. Narrow the filters.");
            return session;
        }
    }

    public string Cursor(Session session, int offset)
    {
        var payload = $"{session.Id:N}:{offset}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." +
            Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    }

    public (Session Session, int Offset) Read(string cursor, Guid userId, int limit, DiscoveryFilters filters)
    {
        try
        {
            if (cursor.Length > 256) throw new FormatException();
            var parts = cursor.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var encoded = parts[0].Replace('-', '+').Replace('_', '/');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload))))
                throw new FormatException();
            var values = payload.Split(':');
            if (values.Length != 2 || !Guid.TryParseExact(values[0], "N", out var id) || !int.TryParse(values[1], out var offset))
                throw new FormatException();
            lock (_gate)
            {
                if (!_sessions.TryGetValue(id, out var session) || session.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
                    throw new ProjectException(400, "DISCOVERY_CURSOR_EXPIRED", "Discovery snapshot expired. Refresh the feed.");
                if (session.UserId != userId || session.Limit != limit || session.Filters != filters || offset < 0 || offset > session.Cards.Count)
                    throw new FormatException();
                return (session, offset);
            }
        }
        catch (FormatException)
        {
            throw new ProjectException(400, "INVALID_DISCOVERY_CURSOR", "Invalid discovery cursor or changed pagination filters.");
        }
    }
}
