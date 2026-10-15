using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tindy.Application.Entities;
using Tindy.Application.Exceptions;
using Tindy.Application.Interfaces.Repositories;
using Tindy.Infrastructure.Persistence;

namespace Tindy.Infrastructure.Repositories;

public sealed class OnboardingRepository : IOnboardingRepository
{
    private readonly TindyDbContext _context;

    public OnboardingRepository(TindyDbContext context) => _context = context;

    public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _context.Users
            .Include(x => x.Profile)
                .ThenInclude(x => x!.UserSkills)
                    .ThenInclude(x => x.Skill)
            .Include(x => x.Profile)
                .ThenInclude(x => x!.PreferredProfessionalRole)
            .Include(x => x.Credential)
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .Include(x => x.OnboardingSteps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public Task<bool> UsernameExistsAsync(Guid exceptUserId, string usernameNormalized, CancellationToken cancellationToken = default) =>
        _context.Users.AsNoTracking().AnyAsync(x => x.UserId != exceptUserId && x.UsernameNormalized == usernameNormalized, cancellationToken);

    public async Task<IReadOnlyList<Skill>> GetActiveSkillsByIdsAsync(
        IReadOnlyCollection<Guid> skillIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Skills
            .AsNoTracking()
            .Where(x => x.IsActive && skillIds.Contains(x.SkillId))
            .ToListAsync(cancellationToken);
    }

    public Task<ProfessionalRole?> GetActiveProfessionalRoleByIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        _context.ProfessionalRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfessionalRoleId == roleId && x.IsActive, cancellationToken);

    public void SyncUserSkills(UserProfile profile, IReadOnlyCollection<Skill> skills)
    {
        var requiredIds = skills.Select(x => x.SkillId).ToHashSet();
        var existingIds = profile.UserSkills.Select(x => x.SkillId).ToHashSet();

        foreach (var link in profile.UserSkills.Where(x => !requiredIds.Contains(x.SkillId)).ToArray())
            _context.UserSkills.Remove(link);

        foreach (var skill in skills.Where(x => !existingIds.Contains(x.SkillId)))
            _context.UserSkills.Add(new UserSkill
            {
                UserId = profile.UserId,
                SkillId = skill.SkillId
            });
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_users_username_normalized"
            })
        {
            throw new UsernameAlreadyExistsException(ex);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Guid userId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Row lock: serialize step writes / completion for this user.
        // Lock is held until COMMIT/ROLLBACK; prevents concurrent duplicate progress markers.
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE user_id = {userId} FOR UPDATE", cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
