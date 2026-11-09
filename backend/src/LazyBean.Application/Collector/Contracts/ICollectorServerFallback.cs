namespace LazyBean.Application.Collector.Contracts;

// Runs a job on the server when no desktop app is connected. Currently: Google ads only (Meta
// is blocked from datacenter IPs, so it has no server fallback).
public interface ICollectorServerFallback
{
    void ScheduleGoogleFallback(Guid jobId);
}
