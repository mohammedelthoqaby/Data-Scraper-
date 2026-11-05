namespace LazyBean.Application.Collection.Orchestration;

public class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string? DispatchToken { get; set; }
    public string Repo { get; set; } = "mohammedelthoqaby/Data-Scraper-";
    public string Ref { get; set; } = "main";
    public string WorkflowFile { get; set; } = "github-collect.yml";
}
