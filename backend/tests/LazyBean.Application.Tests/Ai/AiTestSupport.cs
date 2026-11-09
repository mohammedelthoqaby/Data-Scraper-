using System.Net;
using System.Text;
using LazyBean.Infrastructure.Ai;
using LazyBean.Infrastructure.Ai.Providers;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LazyBean.Application.Tests.Ai;

/// <summary>Shared fixtures for the AI tests. All keys here are obviously fake placeholders.</summary>
internal static class AiTestSupport
{
    public const string FakeGroqKey = "gsk_test_fake_key_1234";
    public const string FakeGeminiKey = "AIzaTestFakeKey0001";

    public static LazyBeanDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new LazyBeanDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static AiKeyProtector CreateProtector() => new(new EphemeralDataProtectionProvider());

    public static IOptions<AiOptions> CreateOptions(
        string? groqKey = FakeGroqKey,
        string? geminiKey = FakeGeminiKey,
        int maxWaitSeconds = 0,
        int groqRequestsPerMinute = 30,
        int groqTokensPerMinute = 8000)
    {
        return Options.Create(new AiOptions
        {
            Groq = new AiProviderOptions
            {
                ApiKey = groqKey,
                RequestsPerMinute = groqRequestsPerMinute,
                TokensPerMinute = groqTokensPerMinute,
                RequestsPerDay = 1000,
            },
            Gemini = new AiProviderOptions
            {
                ApiKey = geminiKey,
                RequestsPerMinute = 15,
                TokensPerMinute = 250000,
                RequestsPerDay = 1000,
            },
            MaxRateWaitSeconds = maxWaitSeconds,
        });
    }

    /// <summary>Fake provider: each call runs the supplied delegate, which returns a response or throws.</summary>
    public sealed class FakeProvider(string name, Func<AiProviderResponse> respond) : IAiProvider
    {
        public string Name { get; } = name;

        public List<string> ApiKeysUsed { get; } = [];

        public int CallCount => ApiKeysUsed.Count;

        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, string apiKey, string model, CancellationToken ct)
        {
            ApiKeysUsed.Add(apiKey);
            return Task.FromResult(respond());
        }
    }

    /// <summary>Fake HTTP transport that replays queued responses and records every request.</summary>
    public sealed class ScriptedHttpHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
        {
            _responses.Enqueue(() =>
            {
                var message = new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                configure?.Invoke(message);
                return message;
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return Task.FromResult(next());
        }
    }
}
