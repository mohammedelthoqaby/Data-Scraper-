using System.Net;
using System.Text.Json;
using LazyBean.Api.Middleware;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LazyBean.IntegrationTests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_InvalidCredentialsException_Returns401_AndLogsWarningWithUsernameOnly()
    {
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var diagnosticsBufferMock = new Mock<IDiagnosticsErrorBuffer>();

        RequestDelegate next = _ => throw new InvalidCredentialsException("suspicious_user");
        var middleware = new ExceptionHandlingMiddleware(next, loggerMock.Object, diagnosticsBufferMock.Object);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<JsonElement>(responseBody);

        Assert.Equal(401, problem.GetProperty("status").GetInt32());

        // Verify Warning was logged with username
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => (v != null ? v.ToString()! : string.Empty).Contains("suspicious_user")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify Error was NEVER logged
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        // Verify diagnostics error buffer was not touched for 401
        diagnosticsBufferMock.Verify(
            x => x.RecordError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<int>()),
            Times.Never);
    }
}
