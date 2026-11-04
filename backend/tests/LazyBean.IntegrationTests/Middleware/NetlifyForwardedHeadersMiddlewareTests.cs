using System.Net;
using LazyBean.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LazyBean.IntegrationTests.Middleware;

public class NetlifyForwardedHeadersMiddlewareTests
{
    private const string Secret = "test-netlify-signing-secret-key-123456789";

    [Fact]
    public void VerifyNetlifySignature_ValidToken_ReturnsTrue()
    {
        var token = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(5));

        var isValid = NetlifyForwardedHeadersMiddleware.VerifyNetlifySignature(
            token,
            Secret,
            DateTimeOffset.UtcNow);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifyNetlifySignature_ExpiredToken_ReturnsFalse()
    {
        var expiredToken = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(-5));

        var isValid = NetlifyForwardedHeadersMiddleware.VerifyNetlifySignature(
            expiredToken,
            Secret,
            DateTimeOffset.UtcNow);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyNetlifySignature_InvalidIssuer_ReturnsFalse()
    {
        var invalidIssuerToken = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "evil-proxy",
            exp: DateTimeOffset.UtcNow.AddMinutes(5));

        var isValid = NetlifyForwardedHeadersMiddleware.VerifyNetlifySignature(
            invalidIssuerToken,
            Secret,
            DateTimeOffset.UtcNow);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyNetlifySignature_WrongSecret_ReturnsFalse()
    {
        var token = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            "some-other-secret-key",
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(5));

        var isValid = NetlifyForwardedHeadersMiddleware.VerifyNetlifySignature(
            token,
            Secret,
            DateTimeOffset.UtcNow);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifyNetlifySignature_TamperedPayload_ReturnsFalse()
    {
        var token = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(5));

        var parts = token.Split('.');
        // Tamper with payload part
        var tamperedToken = $"{parts[0]}.eyIssIjoidGFtcGVyZWQifQ.{parts[2]}";

        var isValid = NetlifyForwardedHeadersMiddleware.VerifyNetlifySignature(
            tamperedToken,
            Secret,
            DateTimeOffset.UtcNow);

        Assert.False(isValid);
    }

    [Fact]
    public async Task Middleware_ValidToken_TrustsXForwardedFor()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Security:NetlifyProxySecret"] = Secret
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var middleware = new NetlifyForwardedHeadersMiddleware(_ => Task.CompletedTask, config);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        var token = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(5));

        context.Request.Headers["x-nf-sign"] = token;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.42, 10.0.0.1";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.InvokeAsync(context);

        Assert.Equal(IPAddress.Parse("198.51.100.42"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task Middleware_InvalidToken_StripsXForwardedForAndDoesNotTrust()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["Security:NetlifyProxySecret"] = Secret
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var middleware = new NetlifyForwardedHeadersMiddleware(_ => Task.CompletedTask, config);

        var context = new DefaultHttpContext();
        var initialIp = IPAddress.Parse("10.0.0.1");
        context.Connection.RemoteIpAddress = initialIp;

        // Expired token
        var expiredToken = NetlifyForwardedHeadersMiddleware.CreateNetlifyToken(
            Secret,
            iss: "netlify",
            exp: DateTimeOffset.UtcNow.AddMinutes(-10));

        context.Request.Headers["x-nf-sign"] = expiredToken;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.42";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.InvokeAsync(context);

        Assert.Equal(initialIp, context.Connection.RemoteIpAddress);
        Assert.False(context.Request.Headers.ContainsKey("X-Forwarded-For"));
        Assert.False(context.Request.Headers.ContainsKey("X-Forwarded-Proto"));
    }
}
