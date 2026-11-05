using System.Net;
using System.Text;
using System.Text.Json;
using LazyBean.Application.Common.Interfaces;
using LazyBean.Domain.Entities;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LazyBean.Application.Tests.Ai;

public class AiClientRateLimitTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public int CallCount { get; private set; }

        public void EnqueueResponse(HttpStatusCode statusCode, string content, TimeSpan? retryAfter = null)
        {
            var msg = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            if (retryAfter.HasValue)
            {
                msg.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            }
            _responses.Enqueue(msg);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count > 0)
            {
                return Task.FromResult(_responses.Dequeue());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("No queued response", Encoding.UTF8, "text/plain")
            });
        }
    }

    private LazyBeanDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new LazyBeanDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task PostWithRetryAsync_When429Returned_RetriesWithBackoffAndSucceedsOnNextAttempt()
    {
        using var db = CreateInMemoryDbContext();

        var handler = new MockHttpMessageHandler();

        // 1st request: 429 Too Many Requests with short Retry-After to speed up test execution
        handler.EnqueueResponse((HttpStatusCode)429, "{\"error\": {\"message\": \"Resource exhausted (rate limit)\"}}", TimeSpan.FromMilliseconds(50));

        // 2nd request: 200 OK with valid Gemini response
        var geminiSuccessJson = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = "OK" } }
                    }
                }
            }
        });
        handler.EnqueueResponse(HttpStatusCode.OK, geminiSuccessJson);

        var httpClient = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("GeminiClient")).Returns(httpClient);

        var mockProtector = new Mock<IAiKeyProtector>();
        mockProtector.Setup(p => p.DecryptKey(It.IsAny<string>())).Returns("valid-gemini-key");

        var client = new GeminiAiClient(mockFactory.Object, db, mockProtector.Object, NullLogger<GeminiAiClient>.Instance);

        // Execute TestConnectionAsync
        var result = await client.TestConnectionAsync("valid-gemini-key", "gemini-3.8-flash");

        // Verify: connection succeeded after retrying the 429 response
        Assert.True(result.Success);
        Assert.Contains("Successfully connected", result.Message);
        Assert.Equal(2, handler.CallCount); // 1st 429, 2nd 200
    }

    [Fact]
    public async Task PostWithRetryAsync_When429RepeatedlyExceedsMaxRetries_FailsGracefully()
    {
        using var db = CreateInMemoryDbContext();

        var handler = new MockHttpMessageHandler();

        // Enqueue 429 for all retry attempts (initial + 3 retries = 4 calls)
        for (int i = 0; i < 5; i++)
        {
            handler.EnqueueResponse((HttpStatusCode)429, "{\"error\": {\"message\": \"Resource exhausted\"}}", TimeSpan.FromMilliseconds(20));
        }

        var httpClient = new HttpClient(handler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("GeminiClient")).Returns(httpClient);

        var mockProtector = new Mock<IAiKeyProtector>();

        var client = new GeminiAiClient(mockFactory.Object, db, mockProtector.Object, NullLogger<GeminiAiClient>.Instance);

        var result = await client.TestConnectionAsync("valid-gemini-key", "gemini-3.8-flash");

        // Verify: reported as failure without unhandled crash
        Assert.False(result.Success);
        Assert.Contains("429", result.Message);
        Assert.Equal(3, handler.CallCount); // Initial + max 2 retries = 3 attempts
    }
}
