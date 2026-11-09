using LazyBean.Infrastructure.External.Http;
using System.Net;
using System.Net.Sockets;
using LazyBean.Application.Collection.Contracts;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class HttpRetryHelperTests
{
    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    public void IsRetryableStatusCode_ReturnsExpected(int statusCode, bool expected)
    {
        var result = HttpRetryHelper.IsRetryableStatusCode(statusCode);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsRetryableException_TimeoutException_ReturnsTrue()
    {
        var ex = new TimeoutException("Operation timed out.");
        Assert.True(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_TaskCanceledException_WhenNotUserCancelled_ReturnsTrue()
    {
        var ex = new TaskCanceledException("HttpClient timeout");
        Assert.True(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_TaskCanceledException_WhenUserCancelled_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("User cancelled");
        Assert.False(HttpRetryHelper.IsRetryableException(ex, cts.Token));
    }

    [Fact]
    public void IsRetryableException_DnsNameResolutionError_ReturnsFalse()
    {
        var ex = new HttpRequestException(HttpRequestError.NameResolutionError, "Name or service not known");
        Assert.False(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_DnsSocketErrorHostNotFound_ReturnsFalse()
    {
        var inner = new SocketException((int)SocketError.HostNotFound);
        var ex = new HttpRequestException("No such host is known.", inner);
        Assert.False(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_ConnectionRefused_ReturnsFalse()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.", inner);
        Assert.False(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_TimeoutExceptionInner_ReturnsTrue()
    {
        var ex = new HttpRequestException("The request was canceled due to configured HttpClient.Timeout", new TimeoutException());
        Assert.True(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_HttpRequestExceptionWith503Status_ReturnsTrue()
    {
        var ex = new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable);
        Assert.True(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }

    [Fact]
    public void IsRetryableException_HttpRequestExceptionWith403Status_ReturnsFalse()
    {
        var ex = new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
        Assert.False(HttpRetryHelper.IsRetryableException(ex, CancellationToken.None));
    }
}
