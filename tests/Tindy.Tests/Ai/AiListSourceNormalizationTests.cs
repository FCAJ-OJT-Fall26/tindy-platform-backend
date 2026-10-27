using System.Text.Json;
using Tindy.Application.DTOs.Ai;
using Xunit;

namespace Tindy.Tests.Ai;

public sealed class AiListSourceNormalizationTests
{
    [Theory]
    [InlineData("2019")]
    [InlineData("2021-01")]
    [InlineData("01/2021")]
    [InlineData("01/02/2021")]
    [InlineData("2025-02-29")]
    public void PartialAmbiguousOrInvalidCertificateDatesStayNull(string source)
    {
        var draft = ParseCertificate(source);
        Assert.Null(Assert.Single(draft.Certificates).IssuedAt);
        Assert.Contains(draft.Warnings, warning => warning.Field == "certificates[0].issued_at" && warning.Code == "INCOMPLETE_DATE");
    }

    [Theory]
    [InlineData("2024-02-29", "2024-02-29")]
    [InlineData("23/10/2021", "2021-10-23")]
    public void CompleteUnambiguousCertificateDatesArePreserved(string source, string expected) =>
        Assert.Equal(expected, Assert.Single(ParseCertificate(source).Certificates).IssuedAt);

    [Fact]
    public void EmployerAndSourceMonthsAreNormalizedWithoutInventingProjectNames()
    {
        var json = JsonSerializer.Serialize(new
        {
            skill_ids = Array.Empty<string>(),
            experiences = new[] { new { employer_name = "Example Company", role_title = "AI Engineer", description = "Built models.\nUsed Python.", start_date_source = "01/2021", end_date_source = "Nay", skill_ids = Array.Empty<string>() } },
            certificates = Array.Empty<object>(), warnings = Array.Empty<object>()
        });
        var value = Assert.Single(AiListDraftValidator.Parse(json, new([])).Experiences);
        Assert.Equal("Example Company: Built models.\nUsed Python.", value.Description);
        Assert.Equal("2021-01", value.StartDate);
        Assert.True(value.IsOngoing);
        Assert.Null(value.EndDate);
        Assert.Null(value.ProjectName);
    }

    private static ProfileDraftResponse ParseCertificate(string source) => AiListDraftValidator.Parse(JsonSerializer.Serialize(new
    {
        skill_ids = Array.Empty<string>(), experiences = Array.Empty<object>(),
        certificates = new[] { new { title = "Certificate", issued_at_source = source } }, warnings = Array.Empty<object>()
    }), new([]));
}
