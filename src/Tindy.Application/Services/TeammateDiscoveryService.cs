using Tindy.Application.DTOs.Projects;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;

namespace Tindy.Application.Services;

public sealed class TeammateDiscoveryService(ITeammateDiscoveryRepository repository, TeammateDiscoveryFeedSessions sessions, TimeProvider clock)
{
    public async Task<TeammateDiscoveryPage> GetAsync(Guid leaderId, Guid projectId, Guid positionId, int limit, string? cursor,
        TeammateDiscoveryFilters filters, CancellationToken ct = default)
    {
        if (filters.SortBy != "match" || filters.MinAvailableHoursPerWeek is < 1 or > 40 || filters.RoleId == Guid.Empty || filters.SkillId == Guid.Empty)
            throw new ProjectException(400, "VALIDATION_FAILED", "Invalid candidate filters or sort_by.");
        limit = Math.Clamp(limit, 1, 50);
        var now = clock.GetUtcNow().UtcDateTime;
        var context = await repository.GetContextAsync(leaderId, projectId, positionId, now, ct);
        var offset = 0;
        TeammateDiscoveryFeedSessions.Session session;
        if (cursor is null)
        {
            var candidates = await repository.GetCandidatesAsync(projectId, positionId, now, filters, ct);
            var cards = candidates.Select(c => new TeammateCandidateCard(c.Candidate, c.InteractionAction,
                PositionMatchScorer.Score(new(c.Candidate.PreferredRoleId, c.Candidate.AvailableHoursPerWeek, c.Candidate.Skills.Select(s => s.SkillId).ToArray()),
                    context.Position, context.ExpectedCommitmentHours)))
                .OrderByDescending(c => c.Matching.MatchScore).ThenBy(c => c.Candidate.UserId).ToArray();
            session = sessions.Create(leaderId, projectId, positionId, now, limit, filters, cards);
            return Page(session, context, candidates, 0);
        }
        (session, offset) = sessions.Read(cursor, leaderId, projectId, positionId, limit, filters);
        var current = await repository.GetCandidatesAsync(projectId, positionId, now, filters, ct);
        return Page(session, context, current, offset);
    }

    private TeammateDiscoveryPage Page(TeammateDiscoveryFeedSessions.Session session, TeammateDiscoveryContext context,
        IReadOnlyList<TeammateCandidateData> current, int offset)
    {
        var eligible = current.ToDictionary(c => c.Candidate.UserId);
        var items = new List<TeammateCandidateCard>();
        while (offset < session.Cards.Count && items.Count < session.Limit)
        {
            var card = session.Cards[offset++];
            if (!eligible.TryGetValue(card.Candidate.UserId, out var live)) continue;
            items.Add(card with { InteractionAction = live.InteractionAction, Matching = card.Matching with
            {
                BestPosition = card.Matching.BestPosition with { RequiredCount = context.Position.RequiredCount,
                    FilledCount = context.Position.FilledCount, RemainingSlots = context.Position.RequiredCount - context.Position.FilledCount }
            }});
        }
        var more = session.Cards.Skip(offset).Any(c => eligible.ContainsKey(c.Candidate.UserId));
        return new(session.ProjectId, session.PositionId, session.ScoredAt, items, more ? sessions.Cursor(session, offset) : null, more);
    }
}
