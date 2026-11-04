using LazyBean.Domain.Common;
using LazyBean.Domain.Enums;

namespace LazyBean.Domain.Entities;

public class Competitor : BaseEntity, IAggregateRoot
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public CompetitorPlatform Platform { get; set; }
    public bool IsReference { get; set; }
    public bool Enabled { get; set; } = true;

    // Politeness policy for the collection pipeline - per-domain delay between requests
    // and how many requests for this competitor may run concurrently.
    public int MinDelayMs { get; set; } = 3000;
    public int MaxConcurrency { get; set; } = 1;

    public void Rename(string name) => Name = name;

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
