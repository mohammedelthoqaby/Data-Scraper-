using LazyBean.Application.Collector.Dtos;
using LazyBean.Collector.App.Api;
using LazyBean.Collector.App.Runtime;

namespace LazyBean.Collector.App.Jobs;

// The device's token was revoked or never valid: the app must pair again. Stops the worker loop.
public sealed class PairingRequiredException(string message) : Exception(message);

// Claims jobs from the server one at a time and runs them. The server decides the order (store first,
// then Meta, then Google); the app only asks for the next job, keeps its claim alive while it works,
// and reports the result. Nothing is stored on the PC between jobs.
public sealed class JobRunner(
    ICollectorApi api,
    StoreJobHandler store,
    MetaJobHandler meta,
    GoogleJobHandler google,
    ActivityLog log,
    CollectorState state,
    Func<TimeSpan, CancellationToken, Task> delay,
    TimeSpan? heartbeatInterval = null)
{
    public static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _heartbeat = heartbeatInterval ?? DefaultHeartbeat;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            CollectorJobDto? job;
            try
            {
                job = await api.ClaimNextJobAsync(ct);
                state.SetConnection(ConnectionState.Connected, "Connected to the server.");
            }
            catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.Unauthorized)
            {
                throw new PairingRequiredException(ex.Message);
            }
            catch (CollectorApiException ex)
            {
                state.SetConnection(ConnectionState.Waiting, $"{ex.Message} Retrying in 10 seconds.");
                log.Warn($"{ex.Message} Retrying in 10 seconds.");
                await delay(IdlePoll, ct);
                continue;
            }

            if (job is null)
            {
                state.SetConnection(ConnectionState.Connected, "Connected. Waiting for work.");
                await delay(IdlePoll, ct);
                continue;
            }

            await ProcessAsync(job, ct);
        }
    }

    private async Task ProcessAsync(CollectorJobDto job, CancellationToken ct)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var keepAlive = KeepAliveAsync(job.Id, jobCts);
        var title = TitleFor(job);
        state.SetJob(new JobProgress(title, "Starting…", 0, null, DateTimeOffset.Now));
        log.Info($"Started: {title}.");

        try
        {
            var outcome = job.Kind switch
            {
                "StoreCollection" => await store.HandleAsync(job, jobCts.Token),
                "MetaAds" => await meta.HandleAsync(jobCts.Token),
                "GoogleAds" => await google.HandleAsync(jobCts.Token),
                _ => throw new NotSupportedException($"This app does not know the job kind '{job.Kind}'."),
            };

            await api.CompleteJobAsync(job.Id, new CompleteCollectorJobRequest("Completed", outcome.Summary, outcome.Pages), ct);
            state.RecordSync(SourceFor(job), outcome.Summary);
            log.Info($"Finished: {outcome.Summary}");
        }
        catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.Unauthorized)
        {
            throw new PairingRequiredException(ex.Message);
        }
        catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.Conflict)
        {
            // The server took the job back (lease expired). Whatever was sent is safe to send again.
            log.Warn($"{title}: the server no longer has this job for this PC. It will be picked up again.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The app is shutting down or the user signed out. The claim expires on the server.
            throw;
        }
        catch (Exception ex) when (ex is not PairingRequiredException)
        {
            // Covers a lost claim detected by the heartbeat as well (cancelled, but not by the app).
            var message = ex is OperationCanceledException ? "The claim on this job was lost." : ex.Message;
            log.Error($"{title} failed: {message}");
            state.RecordSync(SourceFor(job), $"Failed: {message}");
            await ReportFailedAsync(job, message, ct);
        }
        finally
        {
            jobCts.Cancel();
            try
            {
                await keepAlive;
            }
            catch (OperationCanceledException)
            {
                // Expected: the keep-alive loop stops when the job ends.
            }

            state.SetJob(null);
        }
    }

    private async Task ReportFailedAsync(CollectorJobDto job, string message, CancellationToken ct)
    {
        try
        {
            await api.CompleteJobAsync(job.Id, new CompleteCollectorJobRequest("Failed", message, null), ct);
        }
        catch (CollectorApiException)
        {
            // The server cannot be reached; the lease expires and the job is retried or failed there.
        }
    }

    // Keeps the claim alive while the job runs. A conflict means the server has taken the job back.
    private async Task KeepAliveAsync(Guid jobId, CancellationTokenSource jobCts)
    {
        try
        {
            while (!jobCts.IsCancellationRequested)
            {
                await Task.Delay(_heartbeat, jobCts.Token);
                try
                {
                    await api.HeartbeatAsync(jobId, jobCts.Token);
                }
                catch (CollectorApiException ex) when (ex.Kind == ApiErrorKind.Conflict)
                {
                    log.Warn("The server took this job back. Stopping it here.");
                    jobCts.Cancel();
                }
                catch (CollectorApiException ex)
                {
                    log.Warn($"Heartbeat failed ({ex.Message}). Will try again.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The job finished.
        }
    }

    private static string TitleFor(CollectorJobDto job) => job.Kind switch
    {
        "StoreCollection" => $"Store {job.Competitor?.Name ?? "catalog"}",
        "MetaAds" => "Meta ads",
        "GoogleAds" => "Google ads",
        _ => job.Kind,
    };

    // Keys the "last sync" rows in the window and in settings.
    internal static string SourceFor(CollectorJobDto job) => job.Kind switch
    {
        "StoreCollection" => $"store:{job.Competitor?.Slug ?? "unknown"}",
        "MetaAds" => "meta",
        "GoogleAds" => "google",
        _ => job.Kind,
    };
}
