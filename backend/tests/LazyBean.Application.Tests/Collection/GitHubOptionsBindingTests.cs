using LazyBean.Application.Collection.Orchestration;
using LazyBean.Infrastructure.Collection.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace LazyBean.Application.Tests.Collection;

public class GitHubOptionsBindingTests
{
    // A unique prefix keeps these test variables away from any real GitHub__* settings on the machine.
    private const string Prefix = "LAZYBEAN_TEST_";

    private static readonly string[] Variables =
    [
        Prefix + "GitHub__DispatchToken",
        Prefix + "GitHub__Repo",
        Prefix + "GitHub__Ref"
    ];

    [Fact]
    public void EnvironmentVariablesWithDoubleUnderscores_BindToGitHubOptions()
    {
        try
        {
            Environment.SetEnvironmentVariable(Variables[0], "ghp_test_value");
            Environment.SetEnvironmentVariable(Variables[1], "owner/repo-for-test");
            Environment.SetEnvironmentVariable(Variables[2], "release");

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables(Prefix)
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions<GitHubOptions>()
                .Bind(configuration.GetSection(GitHubOptions.SectionName));

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<GitHubOptions>>().Value;

            Assert.Equal("ghp_test_value", options.DispatchToken);
            Assert.Equal("owner/repo-for-test", options.Repo);
            Assert.Equal("release", options.Ref);
        }
        finally
        {
            foreach (var name in Variables)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    [Fact]
    public void Validator_LogsTokenPresence_NeverTheValue()
    {
        const string secret = "ghp_SECRET_SHOULD_NOT_APPEAR";
        var logger = new CapturingLogger<GitHubOptionsValidator>();

        var result = new GitHubOptionsValidator(logger)
            .Validate(null, new GitHubOptions { DispatchToken = secret });

        Assert.True(result.Succeeded);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("dispatchTokenPresent=True", message);
        Assert.DoesNotContain(secret, message);
    }

    [Fact]
    public void Validator_MissingToken_ReportsFalse_AndDoesNotFailStartup()
    {
        var logger = new CapturingLogger<GitHubOptionsValidator>();

        var result = new GitHubOptionsValidator(logger)
            .Validate(null, new GitHubOptions { DispatchToken = null });

        Assert.True(result.Succeeded);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("dispatchTokenPresent=False", message);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
