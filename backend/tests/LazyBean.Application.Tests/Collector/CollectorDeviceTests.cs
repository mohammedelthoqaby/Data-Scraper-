using LazyBean.Application.Collector;
using LazyBean.Application.Collector.Commands.PairCollectorDevice;
using LazyBean.Application.Collector.Commands.RevokeCollectorDevice;
using LazyBean.Application.Collector.Queries.GetCollectorDevices;
using LazyBean.Domain.Entities;
using LazyBean.Domain.Enums;
using LazyBean.Domain.Exceptions;
using LazyBean.Infrastructure.Collector;
using LazyBean.Infrastructure.Persistence;
using LazyBean.Infrastructure.Persistence.Repositories;
using LazyBean.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LazyBean.Application.Tests.Collector;

public class CollectorDeviceTests
{
    private const string Username = "admin";
    private const string Password = "correct-horse-battery";

    private static LazyBeanDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LazyBeanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LazyBeanDbContext(options);
    }

    private static async Task<LazyBeanDbContext> ArrangeDbWithAdminAsync()
    {
        var db = NewDb();
        db.Users.Add(new User
        {
            Username = Username,
            PasswordHash = new PasswordHasherAdapter().Hash(Password),
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public void GeneratedTokens_AreUrlSafeAndUnique()
    {
        var first = CollectorTokens.Generate();
        var second = CollectorTokens.Generate();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
    }

    [Fact]
    public void Hash_IsStableSha256Hex_AndNeverEqualsTheToken()
    {
        var hash = CollectorTokens.Hash("sample-token");

        Assert.Equal(hash, CollectorTokens.Hash("sample-token"));
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.NotEqual("sample-token", hash);
    }

    [Fact]
    public async Task Pair_WithValidCredentials_ReturnsTokenAndStoresOnlyItsHash()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var handler = new PairCollectorDeviceCommandHandler(new UnitOfWork(db), new PasswordHasherAdapter());

        var result = await handler.Handle(
            new PairCollectorDeviceCommand(Username, Password, "  Office PC  "),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal("Office PC", result.DeviceName);
        Assert.Equal(new[] { CollectorDevice.AdsIngestScope, CollectorDevice.StoreIngestScope }, result.Scopes);

        var stored = await db.CollectorDevices.SingleAsync();
        Assert.Equal(CollectorTokens.Hash(result.Token), stored.TokenHash);
        Assert.NotEqual(result.Token, stored.TokenHash);
        Assert.Equal(Username, stored.PairedByUsername);
        Assert.False(stored.IsRevoked);
    }

    [Fact]
    public async Task Pair_WithWrongPassword_ThrowsInvalidCredentials_AndStoresNothing()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var handler = new PairCollectorDeviceCommandHandler(new UnitOfWork(db), new PasswordHasherAdapter());

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => handler.Handle(
            new PairCollectorDeviceCommand(Username, "wrong", "Office PC"),
            CancellationToken.None));

        Assert.Empty(await db.CollectorDevices.ToListAsync());
    }

    [Fact]
    public async Task Authenticate_ValidToken_ReturnsScopedIdentity_AndRecordsLastSeen()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
        var paired = await new PairCollectorDeviceCommandHandler(new UnitOfWork(db), new PasswordHasherAdapter())
            .Handle(new PairCollectorDeviceCommand(Username, Password, "Office PC"), CancellationToken.None);

        // Each request runs in its own scope in production; clear tracking to mirror that.
        db.ChangeTracker.Clear();
        var authenticator = new CollectorDeviceAuthenticator(new UnitOfWork(db), clock);
        var identity = await authenticator.AuthenticateAsync(paired.Token, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.Equal(paired.DeviceId, identity!.DeviceId);
        Assert.Contains(CollectorDevice.AdsIngestScope, identity.Scopes);
        Assert.Contains(CollectorDevice.StoreIngestScope, identity.Scopes);
        Assert.Equal(clock.Now, (await db.CollectorDevices.SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task Authenticate_UnknownOrEmptyToken_ReturnsNull()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var authenticator = new CollectorDeviceAuthenticator(new UnitOfWork(db), new ManualClock(DateTimeOffset.UtcNow));

        Assert.Null(await authenticator.AuthenticateAsync("not-a-real-token", CancellationToken.None));
        Assert.Null(await authenticator.AuthenticateAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task Authenticate_RevokedToken_ReturnsNull()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var paired = await new PairCollectorDeviceCommandHandler(new UnitOfWork(db), new PasswordHasherAdapter())
            .Handle(new PairCollectorDeviceCommand(Username, Password, "Office PC"), CancellationToken.None);
        await new RevokeCollectorDeviceCommandHandler(new UnitOfWork(db), clock)
            .Handle(new RevokeCollectorDeviceCommand(paired.DeviceId), CancellationToken.None);

        var authenticator = new CollectorDeviceAuthenticator(new UnitOfWork(db), clock);

        Assert.Null(await authenticator.AuthenticateAsync(paired.Token, CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_ReturnsHeldJobToQueue_SoAnotherAppCanPickItUp()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var paired = await new PairCollectorDeviceCommandHandler(new UnitOfWork(db), new PasswordHasherAdapter())
            .Handle(new PairCollectorDeviceCommand(Username, Password, "Office PC"), CancellationToken.None);

        var job = new CollectorJob
        {
            GroupId = Guid.NewGuid(),
            Kind = CollectorJobKind.MetaAds,
            Sequence = 2,
            Status = CollectorJobStatus.Claimed,
            ClaimedByDeviceId = paired.DeviceId,
            Attempts = 1,
        };
        db.CollectorJobs.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new RevokeCollectorDeviceCommandHandler(new UnitOfWork(db), clock)
            .Handle(new RevokeCollectorDeviceCommand(paired.DeviceId), CancellationToken.None);

        var released = await db.CollectorJobs.SingleAsync();
        Assert.Equal(CollectorJobStatus.Queued, released.Status);
        Assert.Null(released.ClaimedByDeviceId);
        Assert.True((await db.CollectorDevices.SingleAsync()).IsRevoked);
    }

    [Fact]
    public async Task DeviceList_ReportsConnected_OnlyWhileRecentlySeen()
    {
        await using var db = await ArrangeDbWithAdminAsync();
        var now = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        db.CollectorDevices.AddRange(
            new CollectorDevice { Name = "Fresh", TokenHash = "a", Scopes = "ads:ingest", PairedByUsername = Username, LastSeenAt = now.AddSeconds(-10) },
            new CollectorDevice { Name = "Stale", TokenHash = "b", Scopes = "ads:ingest", PairedByUsername = Username, LastSeenAt = now.AddMinutes(-5) },
            new CollectorDevice { Name = "Revoked", TokenHash = "c", Scopes = "ads:ingest", PairedByUsername = Username, LastSeenAt = now, RevokedAt = now });
        await db.SaveChangesAsync();

        var handler = new GetCollectorDevicesQueryHandler(db, new ManualClock(now));
        var devices = await handler.Handle(new GetCollectorDevicesQuery(), CancellationToken.None);

        Assert.True(devices.Single(d => d.Name == "Fresh").IsConnected);
        Assert.False(devices.Single(d => d.Name == "Stale").IsConnected);
        Assert.False(devices.Single(d => d.Name == "Revoked").IsConnected);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
