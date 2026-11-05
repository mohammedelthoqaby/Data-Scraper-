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
                ModelName: "gemini-3.8-flash",
                MaskedApiKey: null,
                DailyRequestCap: 1000,
                RequestsUsedToday: 0,
                LastTestedAt: null,
                LastTestSuccess: null,
                LastTestMessage: null);
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
            setting.LastTestMessage);
    }
}
