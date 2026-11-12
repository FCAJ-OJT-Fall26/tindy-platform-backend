namespace Tindy.Application.Entities;

public class ProjectResourceLink
{
    public string Type { get; set; } = "other"; // github, figma, drive, jira, demo, other
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
