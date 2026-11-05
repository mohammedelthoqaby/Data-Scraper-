using LazyBean.Application.Alerts.Dtos;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;

namespace LazyBean.Application.Common.Interfaces;

public interface IAlertService
{
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        AlertSeverity? severity = null,
        AlertStatus? status = null,
        bool? isBestseller = null,
        string? search = null,
        CancellationToken ct = default);

    Task<AlertSummaryDto> GetAlertSummaryAsync(CancellationToken ct = default);

    Task<AlertSettingsDto> GetSettingsAsync(CancellationToken ct = default);

    Task<AlertSettingsDto> UpdateSettingsAsync(AlertSettingsDto dto, CancellationToken ct = default);

    Task<DataHealthDto> GetDataHealthAsync(CancellationToken ct = default);

    Task<AlertDto> PerformActionAsync(Guid alertId, string action, string? note, string user, CancellationToken ct = default);

    Task<IReadOnlyList<AlertHistoryDto>> GetHistoryAsync(Guid alertId, CancellationToken ct = default);

    Task<IReadOnlyList<AlertDto>> SyncAlertsAsync(CancellationToken ct = default);
}
