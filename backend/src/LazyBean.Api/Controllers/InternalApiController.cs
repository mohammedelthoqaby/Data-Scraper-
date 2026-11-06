using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Ads.Common;
using LazyBean.Application.Collection.Contracts;
using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Application.Runs.Commands.StartRun;
using LazyBean.Application.Runs.Dtos;
using LazyBean.Application.Runs.Queries.GetRuns;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LazyBean.Api.Common;
using LazyBean.Application.Collection.Orchestration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LazyBean.Api.Controllers;



[ApiController]
[Route("api/internal")]
[AllowAnonymous]
[EnableRateLimiting("internal")]
public class InternalApiController(
    ISender sender,
    IReadDbContext readContext,
    IUnitOfWork unitOfWork,
    IAdsIngestionService adsIngestion,
    IConfiguration configuration,
    ILogger<InternalApiController> logger) : ControllerBase
{
    private bool IsAuthorized()
    {
        var expectedToken = configuration["Internal:Token"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Internal-Token", out var val) || string.IsNullOrWhiteSpace(val))
        {
            return false;
        }

        var providedToken = val.ToString();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    [HttpPost("runs")]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StartRunResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TriggerRun(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var result = await sender.Send(new StartRunCommand(RunTrigger.Scheduled), ct);
        if (result.AlreadyActive)
        {
            return Ok(result);
        }

        return Accepted(result);
    }

    [HttpGet("diagnostics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetDiagnostics()
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        return Ok(new
        {
            config = new
            {
                buildVersion = typeof(InternalApiController).Assembly.GetName().Version?.ToString() ?? "unknown",
                commitHash = BuildInfoHelper.GetCommitHash(configuration)
            }
        });
    }

    [HttpGet("diagnostics/last-errors")]
    [ProducesResponseType(typeof(DiagnosticsSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetLastErrors([FromServices] IDiagnosticsErrorBuffer errorBuffer)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing X-Internal-Token header." });
        }

        return Ok(errorBuffer.GetSummary());
    }

    [HttpGet("runs/latest")]
    [ProducesResponseType(typeof(RunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLatestRun(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var runs = await sender.Send(new GetRunsQuery(), ct);
        var latest = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (latest == null)
        {
            return NotFound(new { error = "No runs found." });
        }

        return Ok(latest);
    }

    [HttpPost("ads/google/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SyncGoogleAds(
        [FromQuery] bool force,
        [FromServices] IGoogleAdsSyncService googleAdsSyncService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var result = await googleAdsSyncService.SyncGoogleAdsAsync(force, ct);
        return Ok(result);
    }

    [HttpGet("ads/google/identities")]
    [ProducesResponseType(typeof(IReadOnlyList<GoogleIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetGoogleIdentities(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var identities = await (
            from i in readContext.Query<AdvertiserIdentity>()
            join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
            where i.Platform == AdPlatform.Google
            select new GoogleIdentityDto(
                i.Id,
                i.CompetitorId,
                c.Key,
                c.Name,
                i.ExternalId,
                i.Name,
                i.Status.ToString(),
                i.BlockedUntil)
        ).ToListAsync(ct);

        return Ok(identities);
    }

    [HttpPost("ads/google")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestGoogleAds([FromBody] GoogleAdsIngestRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (string.IsNullOrWhiteSpace(request.AdvertiserId))
        {
            return BadRequest(new { error = "AdvertiserId is required." });
        }

        var result = await adsIngestion.IngestGoogleAdvertiserAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("ads/meta/identities")]
    [ProducesResponseType(typeof(IReadOnlyList<MetaIdentityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMetaIdentities(CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var identities = await (
            from i in readContext.Query<AdvertiserIdentity>()
            join c in readContext.Query<Competitor>() on i.CompetitorId equals c.Id
            where i.Platform == AdPlatform.Meta && i.Status == AdvertiserIdentityStatus.Approved
            select new MetaIdentityDto(
                i.Id,
                i.CompetitorId,
                c.Key,
                c.Name,
                i.ExternalId,
                i.Name,
                i.Status.ToString(),
                i.BlockedUntil)
        ).ToListAsync(ct);

        return Ok(identities);
    }

    [HttpPost("ads/meta")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestMetaAds([FromBody] MetaAdsIngestRequest request, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (string.IsNullOrWhiteSpace(request.PageId))
        {
            return BadRequest(new { error = "PageId is required." });
        }

        var result = await adsIngestion.IngestMetaPageAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("diagnostics/strong-coffee")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DiagnoseStrongCoffee([FromServices] IHttpClientFactory httpClientFactory, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        var url = "https://strongcoffee-eg.com/wp-json/wc/store/v1/products?per_page=100&page=1";

        async Task<object> ProbeAsync(string ua)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", ua);
                using var resp = await client.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                var snippet = body.Length > 200 ? body[..200] : body;
                resp.Headers.TryGetValues("CF-RAY", out var cfRay);
                return new
                {
                    userAgent = ua,
                    statusCode = (int)resp.StatusCode,
                    status = resp.StatusCode.ToString(),
                    server = resp.Headers.Server?.ToString() ?? "",
                    cfRay = cfRay != null ? string.Join(", ", cfRay) : null,
                    bodySnippet = snippet
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    userAgent = ua,
                    statusCode = 0,
                    error = ex.Message
                };
            }
        }

        var currentUa = await ProbeAsync("LazyBean-Collector/1.0");
        var honestUa = await ProbeAsync("LazyBeanBot/1.0 (+https://lazy-bean.runasp.net)");

        return Ok(new
        {
            targetUrl = url,
            currentUa,
            honestUa
        });
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/products")]
    [ProducesResponseType(typeof(IngestBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestCompetitorProducts(
        Guid runId,
        string slug,
        [FromBody] IngestProductsBatchRequest request,
        [FromServices] ICompetitorProductIngestionService ingestionService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (request?.Products == null)
        {
            return BadRequest(new { error = "Products payload is required." });
        }

        var result = await ingestionService.IngestBatchAsync(
            runId,
            slug,
            request.Page,
            request.TotalPages,
            request.Products,
            ct);

        return Ok(result);
    }

    [HttpPost("runs/{runId:guid}/competitors/{slug}/complete")]
    [ProducesResponseType(typeof(CompleteCompetitorResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteCompetitorCollection(
        Guid runId,
        string slug,
        [FromBody] CompleteCompetitorRunRequest request,
        [FromServices] ICompetitorProductIngestionService ingestionService,
        CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing Internal:Token header." });
        }

        if (request == null)
        {
            return BadRequest(new { error = "Complete payload is required." });
        }

        var result = await ingestionService.CompleteCompetitorAsync(
            runId,
            slug,
            request.Status,
            request.TotalPages,
            request.TotalProducts,
            request.Error,
            ct);

        return Ok(result);
    }

    [HttpGet("competitors/{slug}")]
    [ProducesResponseType(typeof(CompetitorConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCompetitorConfig(string slug, CancellationToken ct)
    {
        if (!IsAuthorized())
        {
            return Unauthorized(new { error = "Invalid or missing X-Internal-Token header." });
        }

        var competitor = await unitOfWork.Competitors.GetByKeyAsync(slug, ct);
        if (competitor == null)
        {
            return NotFound(new { error = $"Competitor '{slug}' not found in database." });
        }

        var dto = new CompetitorConfigDto(
            Slug: competitor.Key,
            Name: competitor.Name,
            BaseUrl: competitor.BaseUrl,
            Platform: competitor.Platform.ToString(),
            CollectionHost: competitor.CollectionHost,
            MinDelayMs: competitor.MinDelayMs,
            MaxConcurrency: competitor.MaxConcurrency,
            Enabled: competitor.Enabled);

        return Ok(dto);
    }

    private static string StripTrackingParams(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}".TrimEnd('/');
        }
        catch
        {
            var qIdx = url.IndexOf('?');
            return (qIdx > 0 ? url[..qIdx] : url).TrimEnd('/');
        }
    }
}
