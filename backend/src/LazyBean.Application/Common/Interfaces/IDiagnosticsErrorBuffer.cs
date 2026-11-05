using System;
using System.Collections.Generic;

namespace LazyBean.Application.Common.Interfaces;

public record DiagnosticErrorRecord(
    DateTimeOffset Timestamp,
    string Endpoint,
    string Method,
    string? CorrelationId,
    string ExceptionType,
    string Message,
    string? StackTrace,
    int StatusCode
);

public record DiagnosticsSummary(
    DateTimeOffset AppStartTime,
    TimeSpan ProcessUptime,
    int ProcessId,
    double WorkingSetMb,
    int TotalRecordedErrors,
    IReadOnlyList<DiagnosticErrorRecord> Errors
);

public interface IDiagnosticsErrorBuffer
{
    DateTimeOffset AppStartTime { get; }
    void RecordError(string endpoint, string method, string? correlationId, Exception ex, int statusCode);
    DiagnosticsSummary GetSummary();
}
