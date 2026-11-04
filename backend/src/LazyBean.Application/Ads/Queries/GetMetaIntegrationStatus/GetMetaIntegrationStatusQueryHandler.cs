using LazyBean.Application.Ads.Dtos;
using LazyBean.Application.Common.Interfaces;
using MediatR;

namespace LazyBean.Application.Ads.Queries.GetMetaIntegrationStatus;

public class GetMetaIntegrationStatusQueryHandler(IMetaAdLibraryClient metaClient)
    : IRequestHandler<GetMetaIntegrationStatusQuery, MetaIntegrationStatusDto>
{
    public async Task<MetaIntegrationStatusDto> Handle(GetMetaIntegrationStatusQuery request, CancellationToken ct)
    {
        var debug = await metaClient.DebugTokenAsync(ct);

        if (!debug.IsValid)
        {
            var isConfigured = !string.Equals(debug.ErrorMessage, "Meta:AccessToken is not configured in user secrets.", StringComparison.OrdinalIgnoreCase);
            return new MetaIntegrationStatusDto(
                Configured: isConfigured,
                State: isConfigured ? "invalid_token" : "missing_token",
                DaysRemaining: null,
                ExpiresAt: null,
                AppId: null,
                Scopes: [],
                StatusNote: debug.ErrorMessage ?? "Meta token validation failed.",
                Warning: isConfigured ? "Meta access token is invalid or expired." : "Meta:AccessToken is not configured in user secrets.");
        }

        double? daysRemaining = null;
        string? warning = null;

        if (debug.ExpiresAt.HasValue)
        {
            daysRemaining = Math.Max(0, (debug.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalDays);
            if (daysRemaining < 10.0)
            {
                warning = $"Warning: Meta token will expire in {daysRemaining.Value:F1} days.";
            }
        }

        var state = warning != null ? "degraded" : "ready";

        return new MetaIntegrationStatusDto(
            Configured: true,
            State: state,
            DaysRemaining: daysRemaining.HasValue ? Math.Round(daysRemaining.Value, 1) : null,
            ExpiresAt: debug.ExpiresAt,
            AppId: debug.AppId,
            Scopes: debug.Scopes,
            StatusNote: "Meta token is valid and active.",
            Warning: warning);
    }
}
