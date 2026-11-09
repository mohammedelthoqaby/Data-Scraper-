using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LazyBean.Application.Ai.Commands;

public record UpdateAiSettingsCommand(
    bool? IsEnabled,
    string? ApiKey,
    string? ModelName,
    int? DailyRequestCap,
    bool? CreativeVisionEnabled = null,
    int? CreativeVisionDailyCap = null) : IRequest<AiSettingsDto>;

public record DeleteAiSettingsCommand : IRequest<bool>;

public record TestAiConnectionCommand(
    string? ApiKey,
    string? ModelName) : IRequest<AiTestResultDto>;

public record TriageReviewQueueCommand(int Limit = 100) : IRequest<AiTriageStats>;

public class AiSettingsCommandHandler :
    IRequestHandler<UpdateAiSettingsCommand, AiSettingsDto>,
    IRequestHandler<DeleteAiSettingsCommand, bool>,
    IRequestHandler<TestAiConnectionCommand, AiTestResultDto>,
    IRequestHandler<TriageReviewQueueCommand, AiTriageStats>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadDbContext _readContext;
    private readonly IAiKeyProtector _protector;
    private readonly IAiClient _aiClient;
    private readonly IAiTriageService _triageService;
    private readonly ILogger<AiSettingsCommandHandler> _logger;

    public AiSettingsCommandHandler(
        IUnitOfWork unitOfWork,
        IReadDbContext readContext,
        IAiKeyProtector protector,
        IAiClient aiClient,
        IAiTriageService triageService,
        ILogger<AiSettingsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _readContext = readContext;
        _protector = protector;
        _aiClient = aiClient;
        _triageService = triageService;
        _logger = logger;
    }

    public async Task<AiSettingsDto> Handle(UpdateAiSettingsCommand request, CancellationToken ct)
    {
        var existingList = await _unitOfWork.AiSettings.GetAllAsync(ct);
        var setting = existingList.FirstOrDefault();

        if (setting == null)
        {
            setting = new AiSetting
            {
                IsEnabled = request.IsEnabled ?? false,
                ModelName = !string.IsNullOrWhiteSpace(request.ModelName) ? request.ModelName.Trim() : "gemini-3.5-flash",
                DailyRequestCap = request.DailyRequestCap ?? 1000,
                CreativeVisionEnabled = request.CreativeVisionEnabled ?? false,
                CreativeVisionDailyCap = request.CreativeVisionDailyCap is > 0 ? request.CreativeVisionDailyCap.Value : 20
            };

            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                var clean = request.ApiKey.Trim();
                setting.EncryptedApiKey = _protector.EncryptKey(clean);
                setting.KeyLastFour = clean.Length >= 4 ? clean[^4..] : clean;
            }

            await _unitOfWork.AiSettings.AddAsync(setting, ct);
        }
        else
        {
            if (request.IsEnabled.HasValue)
                setting.IsEnabled = request.IsEnabled.Value;

            if (!string.IsNullOrWhiteSpace(request.ModelName))
                setting.ModelName = request.ModelName.Trim();

            if (request.DailyRequestCap.HasValue && request.DailyRequestCap > 0)
                setting.DailyRequestCap = request.DailyRequestCap.Value;

            if (request.CreativeVisionEnabled.HasValue)
                setting.CreativeVisionEnabled = request.CreativeVisionEnabled.Value;

            if (request.CreativeVisionDailyCap is > 0)
                setting.CreativeVisionDailyCap = request.CreativeVisionDailyCap.Value;

            if (!string.IsNullOrWhiteSpace(request.ApiKey))
            {
                var clean = request.ApiKey.Trim();
                setting.EncryptedApiKey = _protector.EncryptKey(clean);
                setting.KeyLastFour = clean.Length >= 4 ? clean[^4..] : clean;
            }

            _unitOfWork.AiSettings.Update(setting);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("AI configuration updated (Enabled: {Enabled}, Model: {Model}).", setting.IsEnabled, setting.ModelName);

        var masked = !string.IsNullOrWhiteSpace(setting.KeyLastFour)
            ? $"••••••••••••{setting.KeyLastFour}"
            : null;

        return new AiSettingsDto(
            setting.IsEnabled,
            setting.ModelName,
            masked,
            setting.DailyRequestCap,
            setting.RequestsUsedToday,
            setting.LastTestedAt,
            setting.LastTestSuccess,
            setting.LastTestMessage,
            setting.LastCallAt,
            setting.LastCallStatus,
            setting.CreativeVisionEnabled,
            setting.CreativeVisionDailyCap,
            setting.CreativeVisionUsedToday);
    }

    public async Task<bool> Handle(DeleteAiSettingsCommand request, CancellationToken ct)
    {
        var existingList = await _unitOfWork.AiSettings.GetAllAsync(ct);
        var setting = existingList.FirstOrDefault();
        if (setting != null)
        {
            setting.IsEnabled = false;
            setting.EncryptedApiKey = null;
            setting.KeyLastFour = null;
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("AI API key deleted and AI assistance disabled.");
        }
        return true;
    }

    public async Task<AiTestResultDto> Handle(TestAiConnectionCommand request, CancellationToken ct)
    {
        var result = await _aiClient.TestConnectionAsync(request.ApiKey, request.ModelName, ct);

        var existingList = await _unitOfWork.AiSettings.GetAllAsync(ct);
        var setting = existingList.FirstOrDefault();
        if (setting != null)
        {
            setting.LastTestedAt = DateTimeOffset.UtcNow;
            setting.LastTestSuccess = result.Success;
            setting.LastTestMessage = result.Message;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    public async Task<AiTriageStats> Handle(TriageReviewQueueCommand request, CancellationToken ct)
    {
        return await _triageService.TriagePendingReviewsAsync(request.Limit, ct);
    }
}
