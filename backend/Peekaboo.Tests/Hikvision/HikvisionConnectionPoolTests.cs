using Microsoft.Extensions.Logging.Abstractions;
using Peekaboo.Api.Hikvision;
using Xunit;

namespace Peekaboo.Tests.Hikvision;

public sealed class HikvisionConnectionPoolTests
{
    private static readonly HikvisionConnectionKey Key =
        new("nvr-1", "192.168.10.60", 8000);

    [Fact]
    public async Task Concurrent_leases_share_one_login_until_the_last_release()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var first = await pool.RentAsync(Key, CancellationToken.None);
        var second = await pool.RentAsync(Key, CancellationToken.None);

        Assert.Equal(1, api.LoginCount);
        Assert.Equal(first.Connection.UserId, second.Connection.UserId);
        Assert.Equal(0, api.LogoutCount);

        first.Dispose();
        Assert.Equal(0, api.LogoutCount);

        second.Dispose();
        Assert.Equal(1, api.LogoutCount);
    }

    [Fact]
    public async Task Rent_after_release_logs_in_again()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        using (var first = await pool.RentAsync(Key, CancellationToken.None))
        {
            Assert.Equal(1, api.LoginCount);
        }

        using (var second = await pool.RentAsync(Key, CancellationToken.None))
        {
            Assert.Equal(2, api.LoginCount);
            Assert.NotEqual(1, second.Connection.UserId);
        }

        Assert.Equal(2, api.LogoutCount);
    }

    [Fact]
    public async Task Invalidate_waits_for_the_last_lease_before_logging_out()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var lease = await pool.RentAsync(Key, CancellationToken.None);

        await pool.InvalidateAsync(Key.NvrId);

        Assert.Equal(0, api.LogoutCount);

        lease.Dispose();
        Assert.Equal(1, api.LogoutCount);
    }

    [Fact]
    public async Task Different_nvrs_use_separate_logins()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var first = await pool.RentAsync(Key, CancellationToken.None);
        var second = await pool.RentAsync(Key with { NvrId = "nvr-2" }, CancellationToken.None);

        Assert.Equal(2, api.LoginCount);
        Assert.NotEqual(first.Connection.UserId, second.Connection.UserId);

        first.Dispose();
        second.Dispose();

        Assert.Equal(2, api.LogoutCount);
    }

    [Fact]
    public async Task A_failed_login_is_not_cached()
    {
        var api = new FakeLoginApi { FailuresRemaining = 1 };
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.RentAsync(Key, CancellationToken.None));

        using (await pool.RentAsync(Key, CancellationToken.None))
        {
            Assert.Equal(2, api.LoginCount);
            Assert.Equal(0, api.LogoutCount);
        }

        Assert.Equal(1, api.LogoutCount);
    }

    [Fact]
    public async Task Lease_double_dispose_logs_out_once()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var lease = await pool.RentAsync(Key, CancellationToken.None);
        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, api.LogoutCount);
    }

    [Fact]
    public async Task Invalidating_an_in_flight_login_logs_out_the_orphaned_session()
    {
        var api = new BlockingLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var rentTask = pool.RentAsync(Key, CancellationToken.None);
        Assert.True(api.Started.Wait(TimeSpan.FromSeconds(5)));

        await pool.InvalidateAsync(Key.NvrId);
        api.Continue.Set();

        using (var lease = await rentTask)
        {
            Assert.NotNull(lease.Connection);
        }

        // One login was orphaned by the invalidate, and the replacement was
        // released by the lease. Both must be logged out.
        Assert.Equal(2, api.LoginCount);
        Assert.Equal(2, api.LogoutCount);
    }

    [Fact]
    public async Task Invalidating_an_unknown_nvr_is_a_no_op()
    {
        var api = new FakeLoginApi();
        await using var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        await pool.InvalidateAsync("missing");

        Assert.Equal(0, api.LoginCount);
        Assert.Equal(0, api.LogoutCount);
    }

    [Fact]
    public async Task Disposing_the_pool_defers_logout_until_an_active_lease_is_released()
    {
        var api = new FakeLoginApi();
        var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var lease = await pool.RentAsync(Key, CancellationToken.None);
        await pool.DisposeAsync();

        Assert.Equal(0, api.LogoutCount);

        lease.Dispose();
        Assert.Equal(1, api.LogoutCount);
    }

    [Fact]
    public async Task Disposing_the_pool_releases_idle_logins()
    {
        var api = new FakeLoginApi();
        var pool = new HikvisionConnectionPool(
            api,
            NullLogger<HikvisionConnectionPool>.Instance);

        var lease = await pool.RentAsync(Key, CancellationToken.None);
        lease.Dispose();

        await pool.DisposeAsync();

        Assert.Equal(1, api.LogoutCount);
    }

    private sealed class FakeLoginApi : IHikvisionLoginApi
    {
        private int _nextUserId = 1;

        public int LoginCount { get; private set; }

        public int LogoutCount { get; private set; }

        public int FailuresRemaining { get; set; }

        public HikvisionLoginResult Login(string host, int port)
        {
            LoginCount++;

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Login failed.");
            }

            var userId = _nextUserId++;
            return new HikvisionLoginResult(
                userId,
                new HikvisionDeviceInfo(
                    SerialNumber: "serial",
                    AnalogChannelCount: 0,
                    StartChannel: 1,
                    IpChannelCount: 8,
                    StartDigitalChannel: 33));
        }

        public void Logout(int userId) => LogoutCount++;
    }

    private sealed class BlockingLoginApi : IHikvisionLoginApi
    {
        private int _nextUserId = 1;

        public ManualResetEventSlim Started { get; } = new(false);

        public ManualResetEventSlim Continue { get; } = new(false);

        public int LoginCount { get; private set; }

        public int LogoutCount { get; private set; }

        public HikvisionLoginResult Login(string host, int port)
        {
            LoginCount++;
            Started.Set();
            Continue.Wait(TimeSpan.FromSeconds(10));

            var userId = _nextUserId++;
            return new HikvisionLoginResult(
                userId,
                new HikvisionDeviceInfo("serial", 0, 1, 8, 33));
        }

        public void Logout(int userId) => LogoutCount++;
    }
}
