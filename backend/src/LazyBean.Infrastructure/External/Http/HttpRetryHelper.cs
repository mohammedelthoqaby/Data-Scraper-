using System.Net.Sockets;

namespace LazyBean.Infrastructure.External.Http;

public static class HttpRetryHelper
{
    public static bool IsRetryableStatusCode(int statusCode)
    {
        return statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

    public static bool IsRetryableException(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return false;
        }

        if (ex is TimeoutException)
        {
            return true;
        }

        if (ex is TaskCanceledException)
        {
            return !ct.IsCancellationRequested;
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.InnerException is TimeoutException)
            {
                return true;
            }

            if (httpEx.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError)
            {
                return false;
            }

            if (httpEx.InnerException is SocketException sockEx)
            {
                if (sockEx.SocketErrorCode is SocketError.HostNotFound
                    or SocketError.NoData
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    or SocketError.ConnectionRefused)
                {
                    return false;
                }
            }

            var msg = httpEx.Message;
            if (msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("nodename nor servname provided", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (httpEx.StatusCode.HasValue)
            {
                return IsRetryableStatusCode((int)httpEx.StatusCode.Value);
            }
        }

        return false;
    }
}
