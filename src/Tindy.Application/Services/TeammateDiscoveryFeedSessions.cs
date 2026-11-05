using System.Security.Cryptography;
using System.Text;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;

namespace Tindy.Application.Services;

// Separate from the stable Project feed. State is intentionally process-local.
public sealed class TeammateDiscoveryFeedSessions(TimeProvider clock)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly object _gate = new();
    public sealed record Session(Guid Id, Guid UserId, Guid ProjectId, Guid PositionId, DateTime ScoredAt,
        DateTime ExpiresAt, int Limit, TeammateDiscoveryFilters Filters, IReadOnlyList<TeammateCandidateCard> Cards);

    public Session Create(Guid userId, Guid projectId, Guid positionId, DateTime now, int limit,
        TeammateDiscoveryFilters filters, IReadOnlyList<TeammateCandidateCard> cards)
    {
        if (cards.Count > 100_000) throw new ProjectException(503, "TEAMMATE_DISCOVERY_SNAPSHOT_TOO_LARGE", "Narrow the candidate filters.");
        lock (_gate)
        {
            foreach (var id in _sessions.Values.Where(s => s.ExpiresAt <= now).Select(s => s.Id).ToArray()) _sessions.Remove(id);
            foreach (var id in _sessions.Values.Where(s => s.UserId == userId).OrderByDescending(s => s.ScoredAt).Skip(3).Select(s => s.Id).ToArray()) _sessions.Remove(id);
            while (_sessions.Count > 0 && (_sessions.Count >= 128 || _sessions.Values.Sum(s => s.Cards.Count) + cards.Count > 100_000))
                _sessions.Remove(_sessions.Values.MinBy(s => s.ScoredAt)!.Id);
            var session = new Session(Guid.NewGuid(), userId, projectId, positionId, now, now.AddMinutes(15), limit, filters, cards);
            _sessions.Add(session.Id, session);
            return session;
        }
    }

    public string Cursor(Session session, int offset)
    {
        var payload = $"teammates-v1:{session.Id:N}:{offset}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." +
            Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));
    }

    public (Session Session, int Offset) Read(string cursor, Guid userId, Guid projectId, Guid positionId, int limit, TeammateDiscoveryFilters filters)
    {
        try
        {
            if (cursor.Length > 256) throw new FormatException();
            var parts = cursor.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var encoded = parts[0].Replace('-', '+').Replace('_', '/');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(parts[1]), HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)))) throw new FormatException();
            var values = payload.Split(':');
            if (values.Length != 3 || values[0] != "teammates-v1" || !Guid.TryParseExact(values[1], "N", out var id) || !int.TryParse(values[2], out var offset)) throw new FormatException();
            lock (_gate)
            {
                if (!_sessions.TryGetValue(id, out var session) || session.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
                    throw new ProjectException(400, "TEAMMATE_DISCOVERY_CURSOR_EXPIRED", "Candidate snapshot expired. Refresh the feed.");
                if (session.UserId != userId || session.ProjectId != projectId || session.PositionId != positionId || session.Limit != limit ||
                    session.Filters != filters || offset < 0 || offset > session.Cards.Count) throw new FormatException();
                return (session, offset);
            }
        }
        catch (FormatException) { throw new ProjectException(400, "INVALID_TEAMMATE_DISCOVERY_CURSOR", "Invalid candidate cursor or changed context/filters."); }
    }
}
