using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LazyBean.Application.Runs.Dtos;

public record RunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<CompetitorRunDto> Competitors,
    int? MatchesCount = null,
    string? PhasesJson = null);

public record CompetitorRunDto
{
    public Guid CompetitorId { get; init; }
    public string CompetitorKey { get; init; } = "";
    public string? CompetitorName { get; init; }
    public bool IsReference { get; init; }
    public string Status { get; init; } = "";
    public string Coverage { get; init; } = "";
    public int Pages { get; init; }
    public int Products { get; init; }
    public int Errors { get; init; }

    [JsonConstructor]
    public CompetitorRunDto(
        Guid competitorId,
        string competitorKey,
        string? competitorName,
        bool isReference,
        string status,
        string coverage,
        int pages,
        int products,
        int errors)
    {
        CompetitorId = competitorId;
        CompetitorKey = competitorKey;
        CompetitorName = competitorName;
        IsReference = isReference;
        Status = status;
        Coverage = coverage;
        Pages = pages;
        Products = products;
        Errors = errors;
    }

    public CompetitorRunDto(
        Guid competitorId,
        string competitorKey,
        string status,
        string coverage,
        int pages,
        int products,
        int errors)
        : this(competitorId, competitorKey, competitorKey, false, status, coverage, pages, products, errors)
    {
    }
}
