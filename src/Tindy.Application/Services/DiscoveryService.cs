using System.Security.Cryptography;
using System.Text;
using Tindy.Application.DTOs.Projects;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Application.Services;

public sealed class DiscoveryService(IDiscoveryRepository repository, DiscoveryFeedSessions sessions, TimeProvider clock)
{
    public async Task<DiscoveryPage> GetAsync(Guid userId, int limit, string? cursor, DiscoveryFilters filters, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        filters = filters with { Category = string.IsNullOrWhiteSpace(filters.Category) ? null : filters.Category.Trim() };
        var now = clock.GetUtcNow().UtcDateTime;
        DiscoveryFeedSessions.Session session;
        var offset = 0;
        IReadOnlyList<DiscoveryProject> candidates;
        if (cursor is null)
        {
            candidates = await repository.GetCandidatesAsync(userId, now, now, filters, ct);
            var member = await repository.GetMemberAsync(userId, ct);
            var cards = candidates.Select(p => (Project: p, Match: PositionMatchScorer.Best(member, p.Positions, p.ExpectedCommitmentHours)))
                .Where(p => p.Match != null).Select(p => Card(p.Project, p.Match!, now)).ToArray();
            session = sessions.Create(userId, now, limit, filters, Mix(cards, limit, now));
        }
        else
        {
            (session, offset) = sessions.Read(cursor, userId, limit, filters);
            // Refresh eligibility without reordering the snapshot. Newly published
            // projects only enter after refresh; expired/joined/skipped ones vanish.
            candidates = await repository.GetCandidatesAsync(userId, now, session.Snapshot, filters, ct);
        }
        var eligible = candidates.ToDictionary(p => p.ProjectId);
        bool IsStillAvailable(DiscoveryCard card) => eligible.TryGetValue(card.ProjectId, out var candidate) &&
            candidate.Positions.Any(p => p.PositionId == card.BestPosition.PositionId);
        var page = new List<DiscoveryCard>();
        while (offset < session.Cards.Count && page.Count < limit)
        {
            var card = session.Cards[offset++];
            if (IsStillAvailable(card))
            {
                var current = eligible[card.ProjectId];
                var position = current.Positions.Single(p => p.PositionId == card.BestPosition.PositionId);
                page.Add(card with
                {
                    ClosingDaysLeft = RecruitmentDeadline.DaysLeft(current.RecruitmentClosesAt, now),
                    RecruitmentClosesAt = current.RecruitmentClosesAt,
                    RecruitmentClosingDate = RecruitmentDeadline.ClosingDate(current.RecruitmentClosesAt),
                    CurrentTeamSize = current.CurrentTeamSize,
                    MaxTeamCapacity = current.MaxTeamCapacity,
                    IsSaved = current.IsSaved,
                    BestPosition = card.BestPosition with
                    {
                        FilledCount = position.FilledCount,
                        RequiredCount = position.RequiredCount,
                        RemainingSlots = position.RequiredCount - position.FilledCount
                    }
                });
            }
        }
        var more = session.Cards.Skip(offset).Any(IsStillAvailable);
        return new(page, more ? sessions.Cursor(session, offset) : null, more);
    }

    public static IReadOnlyList<DiscoveryCard> Mix(IEnumerable<DiscoveryCard> source, int limit, DateTime snapshot)
    {
        var remaining = source.ToDictionary(c => c.ProjectId);
        var output = new List<DiscoveryCard>();
        var bestQuota = (int)Math.Round(limit * .60, MidpointRounding.AwayFromZero);
        var newQuota = (int)Math.Round(limit * .25, MidpointRounding.AwayFromZero);
        var exploreQuota = limit - bestQuota - newQuota;
        while (remaining.Count > 0)
        {
            void Take(IEnumerable<DiscoveryCard> cards, int count, string bucket)
            {
                foreach (var card in cards.Take(count).ToArray())
                    if (remaining.Remove(card.ProjectId)) output.Add(card with { FeedBucket = bucket });
            }
            IEnumerable<DiscoveryCard> Ranked() => remaining.Values.OrderByDescending(c => c.MatchScore).ThenBy(c => c.ProjectId);
            var before = output.Count;
            Take(Ranked(), bestQuota, "best_match");
            Take(remaining.Values.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.ProjectId), newQuota, "new_projects");
            // Explore deliberately samples the lower half of the remaining score
            // ranking, deterministically for this snapshot, without a hard cutoff.
            Take(Ranked().Skip(remaining.Count / 2).OrderBy(c => Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{snapshot.Ticks}:{c.ProjectId:N}"))), StringComparer.Ordinal), exploreQuota, "explore");
            Take(Ranked(), limit - (output.Count - before), "best_match");
        }
        return output;
    }

    private static DiscoveryCard Card(DiscoveryProject p, PositionMatch m, DateTime now) => new(
        p.ProjectId, p.Name, p.Tagline, p.Description, p.Category, p.DifficultyLevel, p.ExpectedCommitmentHours, p.Duration,
        p.ColorTheme, p.CurrentTeamSize, p.MaxTeamCapacity, p.Leader, p.CreatedAt,
        RecruitmentDeadline.ClosingDate(p.RecruitmentClosesAt), p.RecruitmentClosesAt, true,
        RecruitmentDeadline.DaysLeft(p.RecruitmentClosesAt, now), m.MatchScore, m.AlgorithmVersion, m.BestPosition,
        m.MatchFactors, m.MatchedSkills, m.MissingSkills, m.DataIncomplete, m.Explanation, "best_match", p.IsSaved);
}
