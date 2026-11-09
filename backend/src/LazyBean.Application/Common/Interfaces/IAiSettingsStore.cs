using LazyBean.Application.Ai.Dtos;

namespace LazyBean.Application.Common.Interfaces;

/// <summary>
/// Persistence for AI settings. Keys are encrypted before storage and only masked
/// values leave this boundary.
/// </summary>
public interface IAiSettingsStore
{
    Task<AiSettingsDto> GetAsync(CancellationToken ct = default);

    Task<AiSettingsDto> UpdateAsync(UpdateAiSettingsRequestDto request, CancellationToken ct = default);

    /// <summary>Saves a new key (when provided) and/or the model name for one provider.</summary>
    Task<AiSettingsDto> SaveProviderAsync(string provider, SaveAiProviderRequestDto request, CancellationToken ct = default);

    Task<AiSettingsDto> RemoveProviderKeyAsync(string provider, CancellationToken ct = default);

    Task RecordTestResultAsync(string provider, AiKeyTestResultDto result, CancellationToken ct = default);
}
