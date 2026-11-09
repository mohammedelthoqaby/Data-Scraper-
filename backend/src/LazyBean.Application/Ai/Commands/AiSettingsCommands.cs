using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using MediatR;

namespace LazyBean.Application.Ai.Commands;

public record UpdateAiSettingsCommand(UpdateAiSettingsRequestDto Body) : IRequest<AiSettingsDto>;

public record SaveAiProviderKeyCommand(string Provider, SaveAiProviderRequestDto Body) : IRequest<AiSettingsDto>;

public record RemoveAiProviderKeyCommand(string Provider) : IRequest<AiSettingsDto>;

public record TestAiProviderKeyCommand(string Provider, TestAiProviderRequestDto Body) : IRequest<AiKeyTestResultDto>;

public record TriageReviewQueueCommand(int? Limit) : IRequest<AiTriageStats>;

public class AiSettingsCommandHandler :
    IRequestHandler<UpdateAiSettingsCommand, AiSettingsDto>,
    IRequestHandler<SaveAiProviderKeyCommand, AiSettingsDto>,
    IRequestHandler<RemoveAiProviderKeyCommand, AiSettingsDto>,
    IRequestHandler<TestAiProviderKeyCommand, AiKeyTestResultDto>,
    IRequestHandler<TriageReviewQueueCommand, AiTriageStats>
{
    private readonly IAiSettingsStore _store;
    private readonly IAiClient _aiClient;
    private readonly IAiTriageService _triageService;

    public AiSettingsCommandHandler(
        IAiSettingsStore store,
        IAiClient aiClient,
        IAiTriageService triageService)
    {
        _store = store;
        _aiClient = aiClient;
        _triageService = triageService;
    }

    public Task<AiSettingsDto> Handle(UpdateAiSettingsCommand request, CancellationToken ct) =>
        _store.UpdateAsync(request.Body, ct);

    public Task<AiSettingsDto> Handle(SaveAiProviderKeyCommand request, CancellationToken ct)
    {
        EnsureKnownProvider(request.Provider);
        return _store.SaveProviderAsync(AiProviders.Normalize(request.Provider), request.Body, ct);
    }

    public Task<AiSettingsDto> Handle(RemoveAiProviderKeyCommand request, CancellationToken ct)
    {
        EnsureKnownProvider(request.Provider);
        return _store.RemoveProviderKeyAsync(AiProviders.Normalize(request.Provider), ct);
    }

    public async Task<AiKeyTestResultDto> Handle(TestAiProviderKeyCommand request, CancellationToken ct)
    {
        EnsureKnownProvider(request.Provider);
        var provider = AiProviders.Normalize(request.Provider);

        var result = await _aiClient.TestProviderKeyAsync(provider, request.Body.ApiKey, request.Body.ModelName, ct);
        await _store.RecordTestResultAsync(provider, result, ct);
        return result;
    }

    public Task<AiTriageStats> Handle(TriageReviewQueueCommand request, CancellationToken ct) =>
        _triageService.TriagePendingReviewsAsync(request.Limit, ct);

    private static void EnsureKnownProvider(string provider)
    {
        if (!AiProviders.IsKnown(provider))
            throw new ArgumentException($"Unknown AI provider '{provider}'. Expected 'groq' or 'gemini'.", nameof(provider));
    }
}
