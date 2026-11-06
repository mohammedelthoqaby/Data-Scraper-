namespace LazyBean.IntegrationTests;

// Password of the lazybean user on the throwaway Postgres that the integration tests use
// (docker-compose locally, a service container in CI). It comes from the environment so no
// database password is kept in source control.
internal static class TestDb
{
    public static string Password =>
        Environment.GetEnvironmentVariable("LAZYBEAN_TEST_DB_PASSWORD") is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                "Set LAZYBEAN_TEST_DB_PASSWORD to the password of the lazybean user on the local Postgres before running the integration tests.");
}
