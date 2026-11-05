using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LazyBean.Application.Common.Interfaces;

namespace LazyBean.Infrastructure.Diagnostics;

public class DiagnosticsErrorBuffer : IDiagnosticsErrorBuffer
{
    private const int MaxCapacity = 50;
    private readonly object _lock = new();
    private readonly LinkedList<DiagnosticErrorRecord> _errors = new();
    private int _totalErrors;

    public DateTimeOffset AppStartTime { get; } = DateTimeOffset.UtcNow;

    public void RecordError(string endpoint, string method, string? correlationId, Exception ex, int statusCode)
    {
        var record = new DiagnosticErrorRecord(
            Timestamp: DateTimeOffset.UtcNow,
            Endpoint: endpoint,
            Method: method,
            CorrelationId: correlationId,
            ExceptionType: ex.GetType().FullName ?? ex.GetType().Name,
            Message: ex.Message,
            StackTrace: ex.StackTrace,
            StatusCode: statusCode
        );

        lock (_lock)
        {
            _totalErrors++;
            _errors.AddFirst(record);
            while (_errors.Count > MaxCapacity)
            {
                _errors.RemoveLast();
            }
        }
    }

    public DiagnosticsSummary GetSummary()
    {
        List<DiagnosticErrorRecord> list;
        int total;
        lock (_lock)
        {
            list = _errors.ToList();
            total = _totalErrors;
        }

        double workingSetMb = 0.0;
        try
        {
            using var proc = Process.GetCurrentProcess();
            workingSetMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 2);
        }
        catch
        {
            // Ignore if process memory cannot be queried in limited sandbox
        }

        return new DiagnosticsSummary(
            AppStartTime: AppStartTime,
            ProcessUptime: DateTimeOffset.UtcNow - AppStartTime,
            ProcessId: Environment.ProcessId,
            WorkingSetMb: workingSetMb,
            TotalRecordedErrors: total,
            Errors: list
        );
    }
}
