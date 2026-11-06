using System.Collections.Concurrent;
using LazyBean.Application.Collection.Orchestration;
using LazyBean.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LazyBean.Infrastructure.Collection.Orchestration;

public class CollectionProgressTracker : ICollectionProgressTracker
{
    private readonly ILogger<CollectionProgressTracker> _logger;
    private readonly ConcurrentDictionary<Guid, ActiveRunState> _runs = new();
    private Guid? _activeRunId;
    private readonly object _lock = new();

    public CollectionProgressTracker(ILogger<CollectionProgressTracker> logger)
    {
        _logger = logger;
    }

    public void InitializeRun(Guid runId, IReadOnlyList<Competitor> competitors, Dictionary<Guid, int>? previousCatalogSizes = null)
    {
        lock (_lock)
        {
            _activeRunId = runId;
            var state = new ActiveRunState(runId, competitors, previousCatalogSizes ?? []);
            _runs[runId] = state;
            _logger.LogInformation("Initialized live progress tracking for CollectionRun {RunId} ({Count} competitors).", runId, competitors.Count);
        }
    }

    public void UpdateCompetitorProgress(
        Guid runId,
        Guid competitorId,
        string status,
        int currentPage,
        int? totalPages,
        int productsSaved,
        string? lastMessage)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return;
        }

        state.UpdateCompetitor(competitorId, status, currentPage, totalPages, productsSaved, lastMessage);
    }

    public void UpdatePhase(Guid runId, string phase, string status, string? message, int? durationMs = null)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return;
        }

        state.UpdatePhase(phase, status, message, durationMs);
    }

    public void SetCompleted(Guid runId, string finalStatus)
    {
        if (_runs.TryGetValue(runId, out var state))
        {
            state.MarkCompleted(finalStatus);
        }

        lock (_lock)
        {
            if (_activeRunId == runId)
            {
                _activeRunId = null;
            }
        }
    }

    public RunProgressDto? GetProgress(Guid runId)
    {
        if (_runs.TryGetValue(runId, out var state))
        {
            return state.ToDto();
        }

        return null;
    }

    public RunProgressDto? GetActiveProgress()
    {
        lock (_lock)
        {
            if (_activeRunId.HasValue && _runs.TryGetValue(_activeRunId.Value, out var state))
            {
                return state.ToDto();
            }
        }

        // Check if any run is currently running in state
        var active = _runs.Values.FirstOrDefault(r => r.Status == "Running" || r.Status == "Queued");
        return active?.ToDto();
    }

    private sealed class ActiveRunState
    {
        public Guid RunId { get; }
        public string Status { get; set; } = "Running";
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastProgressAt { get; set; } = DateTimeOffset.UtcNow;
        public string? LastLogMessage { get; set; }

        private readonly ConcurrentDictionary<Guid, CompetitorState> _competitors = new();
        private readonly ConcurrentDictionary<string, PhaseProgressDto> _phases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, int> _previousSizes;
        private readonly double _totalCatalogExpected;

        private const double CollectionWeight = 0.70;
        private const double ClassificationWeight = 0.05;
        private const double MatchingWeight = 0.15;
        private const double AiTriageWeight = 0.05;
        private const double AlertsWeight = 0.05;

        public ActiveRunState(Guid runId, IReadOnlyList<Competitor> competitors, Dictionary<Guid, int> previousSizes)
        {
            RunId = runId;
            _previousSizes = previousSizes;

            double sum = 0;
            foreach (var c in competitors)
            {
                var prev = previousSizes.GetValueOrDefault(c.Id, c.IsReference ? 1500 : 3000);
                sum += Math.Max(prev, 100);

                _competitors[c.Id] = new CompetitorState(c.Id, c.Key, c.Name, c.IsReference, c.CollectionHost ?? "server");
            }

            _totalCatalogExpected = sum > 0 ? sum : competitors.Count * 2500.0;

            // Initialize default post-processing phases
            _phases["classification"] = new PhaseProgressDto("classification", "Queued", "Waiting for collection to finish", null);
            _phases["matching"] = new PhaseProgressDto("matching", "Queued", "Waiting for classification", null);
            _phases["ai_triage"] = new PhaseProgressDto("ai_triage", "Queued", "Waiting for matching", null);
            _phases["alerts"] = new PhaseProgressDto("alerts", "Queued", "Waiting for matching/triage", null);
        }

        public void UpdateCompetitor(Guid competitorId, string status, int currentPage, int? totalPages, int productsSaved, string? lastMessage)
        {
            if (_competitors.TryGetValue(competitorId, out var comp))
            {
                comp.Status = status;
                comp.CurrentPage = currentPage;
                if (totalPages.HasValue && totalPages.Value > 0) comp.TotalPages = totalPages.Value;
                comp.ProductsSaved = productsSaved;
                comp.LastUpdated = DateTimeOffset.UtcNow;
                if (!string.IsNullOrWhiteSpace(lastMessage))
                {
                    comp.LastMessage = lastMessage;
                    LastLogMessage = $"[{comp.CompetitorName}] {lastMessage}";
                }
            }

            LastProgressAt = DateTimeOffset.UtcNow;
        }

        public void UpdatePhase(string phase, string status, string? message, int? durationMs)
        {
            _phases[phase] = new PhaseProgressDto(phase, status, message, durationMs);
            LastProgressAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(message))
            {
                LastLogMessage = $"[{phase}] {message}";
            }
        }

        public void MarkCompleted(string finalStatus)
        {
            Status = finalStatus;
            LastProgressAt = DateTimeOffset.UtcNow;
            LastLogMessage = $"Run {RunId} finished with status {finalStatus}.";
        }

        public RunProgressDto ToDto()
        {
            var now = DateTimeOffset.UtcNow;
            var anyDesktopWaiting = _competitors.Values.Any(c =>
                string.Equals(c.CollectionHost, "desktop", StringComparison.OrdinalIgnoreCase) &&
                (c.Status == "Running" || c.Status == "Queued"));

            var isStalled = !anyDesktopWaiting && (Status == "Running" || Status == "Queued") && (now - LastProgressAt > TimeSpan.FromMinutes(3));

            var compDtos = _competitors.Values.Select(c => c.ToDto()).ToList();

            // Calculate overall percentage
            double percent = 0;

            if (Status == "Completed")
            {
                percent = 100;
            }
            else
            {
                // 1. Competitors collection progress (weighted by catalog size)
                foreach (var comp in _competitors.Values)
                {
                    var prev = _previousSizes.GetValueOrDefault(comp.CompetitorId, comp.IsReference ? 1500 : 3000);
                    var compWeight = CollectionWeight * (prev / _totalCatalogExpected);

                    double compFrac = 0;
                    if (comp.Status == "Completed")
                    {
                        compFrac = 1.0;
                    }
                    else if (comp.TotalPages.HasValue && comp.TotalPages.Value > 0)
                    {
                        compFrac = Math.Min(1.0, (double)comp.CurrentPage / comp.TotalPages.Value);
                    }
                    else if (prev > 0)
                    {
                        compFrac = Math.Min(0.95, (double)comp.ProductsSaved / prev);
                    }
                    else if (comp.CurrentPage > 0)
                    {
                        compFrac = Math.Min(0.95, comp.CurrentPage / 40.0);
                    }

                    percent += compFrac * compWeight * 100.0;
                }

                // 2. Post-processing phases progress
                percent += CalculatePhaseContribution("classification", ClassificationWeight);
                percent += CalculatePhaseContribution("matching", MatchingWeight);
                percent += CalculatePhaseContribution("ai_triage", AiTriageWeight);
                percent += CalculatePhaseContribution("alerts", AlertsWeight);
            }

            var overallInt = Math.Clamp((int)Math.Round(percent), 0, 100);

            var phaseList = new List<PhaseProgressDto>
            {
                _phases.GetValueOrDefault("classification", new PhaseProgressDto("classification", "Queued", null, null)),
                _phases.GetValueOrDefault("matching", new PhaseProgressDto("matching", "Queued", null, null)),
                _phases.GetValueOrDefault("ai_triage", new PhaseProgressDto("ai_triage", "Queued", null, null)),
                _phases.GetValueOrDefault("alerts", new PhaseProgressDto("alerts", "Queued", null, null))
            };

            return new RunProgressDto(
                RunId,
                Status,
                overallInt,
                isStalled,
                LastLogMessage,
                LastProgressAt,
                compDtos,
                phaseList,
                StartedAt);
        }

        private double CalculatePhaseContribution(string phaseName, double phaseWeight)
        {
            if (!_phases.TryGetValue(phaseName, out var p)) return 0;
            if (p.Status == "Completed" || p.Status == "Skipped")
            {
                return phaseWeight * 100.0;
            }
            if (p.Status == "Running")
            {
                return (phaseWeight * 0.5) * 100.0;
            }
            return 0;
        }
    }

    private sealed class CompetitorState(Guid competitorId, string key, string name, bool isReference, string collectionHost = "server")
    {
        public Guid CompetitorId { get; } = competitorId;
        public string CompetitorKey { get; } = key;
        public string CompetitorName { get; } = name;
        public bool IsReference { get; } = isReference;
        public string CollectionHost { get; } = collectionHost;
        public string Status { get; set; } = "Queued";
        public int CurrentPage { get; set; }
        public int? TotalPages { get; set; }
        public int ProductsSaved { get; set; }
        public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
        public string? LastMessage { get; set; }

        public CompetitorProgressDto ToDto() => new(
            CompetitorId,
            CompetitorKey,
            CompetitorName,
            IsReference,
            Status,
            CurrentPage,
            TotalPages,
            ProductsSaved,
            LastUpdated,
            LastMessage,
            CollectionHost);
    }
}
