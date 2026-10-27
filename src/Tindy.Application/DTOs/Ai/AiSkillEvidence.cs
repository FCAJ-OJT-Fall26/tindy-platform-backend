using System.Text.RegularExpressions;

namespace Tindy.Application.DTOs.Ai;

internal static class AiSkillEvidence
{
    // Explicit equivalent names, not related technologies or inferred parent/child skills.
    private static readonly IReadOnlyDictionary<string, string[]> Equivalents = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["PostgreSQL"] = ["Postgres"], ["SQL Server"] = ["Microsoft SQL Server", "MSSQL"],
        ["SQL"] = ["Structured Query Language"], ["AWS"] = ["Amazon Web Services"],
        ["JavaScript"] = ["JS"], ["TypeScript"] = ["TS"], ["Machine Learning"] = ["ML"],
        ["ASP.NET Core"] = ["ASP .NET Core"], [".NET"] = ["dotnet"],
        ["REST API"] = ["RESTful API"], ["CI/CD"] = ["Continuous Integration and Continuous Delivery", "Continuous Integration/Continuous Delivery"]
    };

    public static bool Matches(string name, string quote)
    {
        IEnumerable<string> labels = Equivalents.TryGetValue(name, out var aliases) ? new[] { name }.Concat(aliases) : [name];
        return labels.Any(label => Regex.IsMatch(quote,
            @"(?<![\p{L}\p{N}])" + string.Join(@"\s+", label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
