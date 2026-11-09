using System.Security.Claims;
using System.Text.Encodings.Web;
using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LazyBean.Api.Auth;

// Authenticates the paired collector app. Only answers for /api/collector/* so a device token can
// never open a dashboard endpoint, and a cookie can never open a collector endpoint.
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICollectorDeviceAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceToken";
    public const string PathPrefix = "/api/collector";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await authenticator.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Device token is invalid or has been revoked.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.DeviceId.ToString()),
            new(ClaimTypes.Name, identity.Name),
            new(CollectorClaims.DeviceId, identity.DeviceId.ToString()),
        };
        claims.AddRange(identity.Scopes.Select(scope => new Claim(CollectorClaims.Scope, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
