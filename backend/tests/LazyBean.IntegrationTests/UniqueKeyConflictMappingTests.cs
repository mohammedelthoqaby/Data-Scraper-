using LazyBean.Api.Middleware;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LazyBean.IntegrationTests;

// Proves end to end that a genuine Postgres unique-constraint violation (not a mock) on
// Competitor.Key becomes a DbUpdateException, and that ExceptionHandlingMiddleware maps
// that exact exception to a 409 response. Runs against the dedicated "lazybean_test"
// database (TestDatabaseFixture), never the dev database.
[Collection("Database")]
public class UniqueKeyConflictMappingTests(TestDatabaseFixture db)
{
    [Fact]
    public async Task DuplicateKey_RaisesRealUniqueViolation_AndMiddlewareMapsItTo409()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseNpgsql(db.ConnectionString)
            .Options;

        var key = $"race-test-{Guid.NewGuid():N}";

        await using var first = new LazyBeanDbContext(options);
        first.Competitors.Add(new Competitor
        {
            Key = key,
            Name = "Race Test A",
            BaseUrl = "https://example.com",
            Platform = CompetitorPlatform.Shopify
        });
        await first.SaveChangesAsync();

        await using var second = new LazyBeanDbContext(options);
        second.Competitors.Add(new Competitor
        {
            Key = key, // same key -> violates the unique index from CompetitorConfiguration
            Name = "Race Test B",
            BaseUrl = "https://example.com",
            Platform = CompetitorPlatform.Shopify
        });

        var dbUpdateException = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // Real driver exception from the real database - not a mock.
        var postgresException = Assert.IsType<Npgsql.PostgresException>(dbUpdateException.InnerException);
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, postgresException.SqlState);

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw dbUpdateException,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }
}
