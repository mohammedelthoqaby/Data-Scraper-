using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LazyBean.Application.Ai.Queries;

public record GetAiSettingsQuery : IRequest<AiSettingsDto>;

public class GetAiSettingsQueryHandler : IRequestHandler<GetAiSettingsQuery, AiSettingsDto>
{
    private readonly IReadDbContext _readContext;

    public GetAiSettingsQueryHandler(IReadDbContext readContext)
    {
        _readContext = readContext;
    }

    public async Task<AiSettingsDto> Handle(GetAiSettingsQuery request, CancellationToken ct)
    {
        var setting = await _readContext.Query<AiSetting>().FirstOrDefaultAsync(ct);
        if (setting == null)
        {
            return new AiSettingsDto(
                IsEnabled: false,
                ModelName: "gemini-3.5-flash",
                MaskedApiKey: null,
                DailyRequestCap: 1000,
                RequestsUsedToday: 0,
                LastTestedAt: null,
                LastTestSuccess: null,
                LastTestMessage: null,
                LastCallAt: null,
                LastCallStatus: null,
                CreativeVisionEnabled: false,
                CreativeVisionDailyCap: 20,
                CreativeVisionUsedToday: 0);
        }

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
}
