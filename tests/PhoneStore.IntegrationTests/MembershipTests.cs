using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhoneStore.Api.Entities;
using PhoneStore.Api.DTOs.Membership;
using PhoneStore.Api.Services.Membership;
using PhoneStore.Api.Services.Checkout;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
namespace PhoneStore.IntegrationTests;

public sealed class MembershipTests : IAsyncLifetime
{
    private readonly SqlFixture sql = new();
    public Task InitializeAsync() => sql.InitializeAsync();
    public Task DisposeAsync() => sql.DisposeAsync();
    private async Task<Guid> Seed(decimal amount, OrderStatus status = OrderStatus.Completed, decimal discount = 0, decimal refunded = 0, decimal shippingRefund = 0)
    {
        await using var db = sql.CreateContext(); var id = Guid.NewGuid(); var email = id + "@example.invalid";
        var user = new ApplicationUser { Id = id, FullName = "Synthetic membership", Email = email, NormalizedEmail = email.ToUpperInvariant() };
        db.Users.Add(user); db.CustomerProfiles.Add(new() { UserId = id, EligibleSpend = 0, TierCalculatedAt = DateTime.UtcNow });
        var order = new Order { UserId = id, OrderNumber = "PS" + id.ToString("N")[..28], CustomerEmail = email, NormalizedCustomerEmail = email.ToUpperInvariant(), RecipientName = "Synthetic", Phone = "000", AddressLine = "Synthetic", Province = "Synthetic", Status = status,
            Subtotal = amount + discount, DiscountTotal = discount, ShippingFee = 30000, GrandTotal = amount + 30000, CompletedAt = status == OrderStatus.Completed ? DateTime.UtcNow : null, CreatedAt = DateTime.UtcNow };
        db.Orders.Add(order);
        if (refunded + shippingRefund > 0) {
            var payment = new Payment { Order = order, Amount = order.GrandTotal, Method = PaymentMethod.COD, Status = PaymentStatus.Confirmed, CreatedAt = DateTime.UtcNow };
            db.Refunds.Add(new() { Payment = payment, Status = RefundStatus.Completed, MerchandiseAmount = refunded, ShippingAmount = shippingRefund, Reason = "Synthetic", EventKey = "Synthetic:" + id, RequestHash = new byte[32], CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync(); return id;
    }
    [Fact]
    public async Task Backfill_tests_every_threshold_minus_one_exact_plus_one_and_excludes_shipping_discount_cancelled_incomplete()
    {
        var levels = new decimal[] { 1_000_000, 10_000_000, 50_000_000, 100_000_000 };
        var ids = new List<(Guid Id, decimal Spend, string? Tier)>(); var codes = new[] { "Bronze", "Silver", "Gold", "Diamond" };
        for (var i = 0; i < levels.Length; i++) foreach (var delta in new[] { -1, 0, 1 }) {
            var amount = levels[i] + delta; ids.Add((await Seed(amount, discount: 500), amount, delta == -1 ? i == 0 ? null : codes[i - 1] : codes[i]));
        }
        var cancelled = await Seed(50_000_000, OrderStatus.Cancelled); var pending = await Seed(100_000_000, OrderStatus.Delivered);
        using var host = new AuthFactory(sql.ConnectionString); using var scope = host.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<MembershipService>();
        await service.BootstrapAsync(default); await service.BootstrapAsync(default);
        foreach (var item in ids) { var m = await service.ReadAsync(item.Id, default); Assert.True(m.Enabled); Assert.Equal(item.Spend, m.EligibleSpend); Assert.Equal(item.Tier, m.TierCode); if (m.NextTier is not null) Assert.Equal(m.NextTier.MinimumSpend - m.EligibleSpend, m.RemainingSpend); }
        foreach (var id in new[] { cancelled, pending }) { var m = await service.ReadAsync(id, default); Assert.Equal(0, m.EligibleSpend); Assert.Null(m.TierCode); }
        await using var db = sql.CreateContext(); Assert.Equal(12, await db.CustomerSpendEntries.CountAsync()); Assert.Equal(11, await db.CustomerTierHistories.CountAsync());
    }
    [Fact]
    public async Task Refund_backfill_downgrades_to_unranked_and_ship_only_refund_does_not_change_rank()
    {
        var down = await Seed(1_000_000, refunded: 1); var ship = await Seed(10_000_000, shippingRefund: 30000);
        using var host = new AuthFactory(sql.ConnectionString); using var scope = host.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<MembershipService>();
        await service.BootstrapAsync(default); await service.BootstrapAsync(default);
        var a = await service.ReadAsync(down, default); Assert.Equal(999999, a.EligibleSpend); Assert.Null(a.TierCode);
        var b = await service.ReadAsync(ship, default); Assert.Equal(10000000, b.EligibleSpend); Assert.Equal("Silver", b.TierCode);
        await using var db = sql.CreateContext(); Assert.Equal(3, await db.CustomerSpendEntries.CountAsync());
    }
    private static TierConfigurationRequest Input(TierConfigurationDto configuration, Guid? key = null, decimal[]? amounts = null) => new() {
        OperationKey = key ?? Guid.NewGuid(), Reason = "Synthetic controlled recalculation",
        Tiers = configuration.Tiers.Select((t, i) => new TierInput { Code = t.Code, Version = t.Version, MinimumSpend = amounts?[i] ?? t.MinimumSpend }).ToArray()
    };
    [Fact]
    public async Task Threshold_swap_recalculates_history_keeps_purchase_snapshot_and_replay_concurrent_has_one_result()
    {
        var user = await Seed(10_000_000);
        using var host = new AuthFactory(sql.ConnectionString);
        using (var scope = host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<MembershipService>().BootstrapAsync(default);
        TierConfigurationDto config; using (var scope = host.Services.CreateScope()) config = await scope.ServiceProvider.GetRequiredService<MembershipService>().ConfigurationAsync(default);
        await using (var db = sql.CreateContext()) { var order = await db.Orders.SingleAsync(); order.TierCodeSnapshot = CustomerTierCode.Silver; order.TierIdAtPurchase = (await db.CustomerTiers.SingleAsync(t => t.Code == CustomerTierCode.Silver)).Id; await db.SaveChangesAsync(); }
        var input = Input(config, amounts: [1_000_000, 50_000_000, 100_000_000, 150_000_000]);
        async Task<TierConfigurationDto> Write() { using var scope = host.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<MembershipService>().ConfigureAsync(input, null, default); }
        var results = await Task.WhenAll(Write(), Write()); Assert.Equal(results[0].Tiers.Select(t => t.Version), results[1].Tiers.Select(t => t.Version));
        using (var scope = host.Services.CreateScope()) {
            var service = scope.ServiceProvider.GetRequiredService<MembershipService>(); Assert.Equal("Bronze", (await service.ReadAsync(user, default)).TierCode);
            Assert.Equal(412, (await Assert.ThrowsAsync<CheckoutException>(() => service.ConfigureAsync(Input(config), null, default))).Status);
        }
        await using var verify = sql.CreateContext(); Assert.Single(await verify.CustomerSpendEntries.ToListAsync()); Assert.Equal(2, await verify.CustomerTierHistories.CountAsync()); Assert.Equal(CustomerTierCode.Silver, (await verify.Orders.SingleAsync()).TierCodeSnapshot); Assert.Single(await verify.AuditLogs.Where(a => a.CorrelationId == "Membership.Configuration:" + input.OperationKey.ToString("D")).ToListAsync());
    }
    [Fact]
    public async Task Invalid_thresholds_and_duplicate_codes_reject_before_any_write()
    {
        using var host = new AuthFactory(sql.ConnectionString); using var scope = host.Services.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<MembershipService>(); await service.BootstrapAsync(default);
        var config = await service.ConfigurationAsync(default);
        foreach (var values in new decimal[][] { [0, 10, 50, 100], [1, 10, 10, 100], [1, 10, 5, 100], [1.5m, 10, 50, 100], [1, 10, 50, 9_007_199_254_740_992m] }) Assert.Equal(400, (await Assert.ThrowsAsync<CheckoutException>(() => service.ConfigureAsync(Input(config, amounts: values), null, default))).Status);
        var duplicate = new TierConfigurationRequest { OperationKey = Guid.NewGuid(), Reason = "Synthetic", Tiers = Enumerable.Repeat(new TierInput { Code = "Bronze", MinimumSpend = 1 }, 4).ToArray() };
        Assert.Equal(400, (await Assert.ThrowsAsync<CheckoutException>(() => service.ConfigureAsync(duplicate, null, default))).Status);
        Assert.Equal(config.Tiers.Select(t => t.Version), (await service.ConfigurationAsync(default)).Tiers.Select(t => t.Version));
    }
    [Fact]
    public async Task Backfill_conflict_rolls_back_threshold_versions_history_and_partial_entries()
    {
        var user = await Seed(10_000_000);
        using var host = new AuthFactory(sql.ConnectionString);
        using (var scope = host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<MembershipService>().BootstrapAsync(default);
        TierConfigurationDto config; using (var scope = host.Services.CreateScope()) config = await scope.ServiceProvider.GetRequiredService<MembershipService>().ConfigurationAsync(default);
        await using (var db = sql.CreateContext()) { var entry = await db.CustomerSpendEntries.SingleAsync(); entry.Amount = 1; await db.SaveChangesAsync(); }
        using (var scope = host.Services.CreateScope()) {
            var service = scope.ServiceProvider.GetRequiredService<MembershipService>(); var error = await Assert.ThrowsAsync<CheckoutException>(() => service.ConfigureAsync(Input(config, amounts: [1, 10, 50, 100]), null, default)); Assert.Equal("SPEND_LEDGER_CONFLICT", error.Code);
        }
        using (var scope = host.Services.CreateScope()) Assert.Equal(config.Tiers.Select(t => t.Version), (await scope.ServiceProvider.GetRequiredService<MembershipService>().ConfigurationAsync(default)).Tiers.Select(t => t.Version));
        await using var verify = sql.CreateContext(); Assert.Equal(10000000, (await verify.CustomerProfiles.SingleAsync(p => p.UserId == user)).EligibleSpend); Assert.Single(await verify.CustomerTierHistories.ToListAsync());
    }
    [Fact]
    public async Task Upgrade_preserves_old_Bronze_history_and_down_refuses_unranked_data()
    {
        var old = new SqlFixture(); await old.InitializeAtAsync("20261010040108_CatalogImageUploadOperations");
        try {
            await using var db = old.CreateContext(); var legacyId = Guid.NewGuid(); var user = new ApplicationUser { Id = legacyId, FullName = "Synthetic", Email = legacyId + "@example.invalid", NormalizedEmail = legacyId.ToString().ToUpperInvariant() + "@EXAMPLE.INVALID" }; var tier = new CustomerTier { Code = CustomerTierCode.Bronze, Name = "Bronze", Rank = 1, MinimumSpend = 0 }; var profile = new CustomerProfile { User = user, Tier = tier, EligibleSpend = 0, TierCalculatedAt = DateTime.UtcNow }; db.CustomerProfiles.Add(profile); await db.SaveChangesAsync(); var version = profile.Version.ToArray();
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); await db.Entry(profile).ReloadAsync(); Assert.Equal(version, profile.Version); Assert.Equal(tier.Id, profile.TierId);
            profile.TierId = null; db.CustomerTierHistories.Add(new() { UserId = user.Id, PreviousTierId = tier.Id, NewTierId = null, EligibleSpend = 0, Reason = "Synthetic policy downgrade", CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20261010040108_CatalogImageUploadOperations")); Assert.Equal(51092, error.Number); Assert.Equal(6, (await db.Database.GetAppliedMigrationsAsync()).Count()); Assert.Null((await db.CustomerProfiles.AsNoTracking().SingleAsync()).TierId);
        } finally { await old.DisposeAsync(); }
    }
}
