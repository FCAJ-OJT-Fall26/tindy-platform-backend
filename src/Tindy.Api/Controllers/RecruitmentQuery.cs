using Tindy.Application.Exceptions;

namespace Tindy.Api.Controllers;

internal static class RecruitmentQuery
{
    internal static void RequireOnly(HttpRequest request, params string[] allowed)
    {
        if (request.Query.Any(q => !allowed.Contains(q.Key, StringComparer.Ordinal) || q.Value.Count != 1 || string.IsNullOrWhiteSpace(q.Value.ToString())))
            throw new ProjectException(400, "VALIDATION_FAILED", "Unsupported, empty or repeated query parameter.");
    }
}
