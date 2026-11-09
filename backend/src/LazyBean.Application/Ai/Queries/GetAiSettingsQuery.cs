using LazyBean.Application.Ai.Dtos;
using LazyBean.Application.Common.Interfaces;
using MediatR;

namespace LazyBean.Application.Ai.Queries;

public record GetAiSettingsQuery : IRequest<AiSettingsDto>;

public class GetAiSettingsQueryHandler : IRequestHandler<GetAiSettingsQuery, AiSettingsDto>
{
    private readonly IAiSettingsStore _store;

    public GetAiSettingsQueryHandler(IAiSettingsStore store)
    {
        _store = store;
    }

    public Task<AiSettingsDto> Handle(GetAiSettingsQuery request, CancellationToken ct) =>
        _store.GetAsync(ct);
}
