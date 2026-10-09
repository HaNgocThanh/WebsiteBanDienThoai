using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhoneStore.Api.Entities;

namespace PhoneStore.IntegrationTests;

[Collection("SQL")]
public class SchemaTests(SqlFixture fixture)
{
    [Fact]
    public async Task MigrationsReplayWithoutChangingStoredDataOrModel()
    {
        await using var db = fixture.CreateContext();
        var name = "replay-" + Guid.NewGuid().ToString("N");
        db.Brands.Add(new Brand { Name = name, Slug = name });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        Assert.True(await db.Brands.AnyAsync(x => x.Name == name));
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private async Task<ProductVariant> AddVariant()
    {
        await using var db = fixture.CreateContext();
        var key = Guid.NewGuid().ToString("N");
        var product = new Product
        {
            Name = "Synthetic phone", Slug = key, Description = "Test fixture",
            Brand = new Brand { Name = key, Slug = key }, Category = new Category { Name = key, Slug = key }
        };
        var variant = new ProductVariant { Product = product, Sku = key, Color = "Black", StorageGb = 128, RamGb = 8, Price = 1000000 };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync();
        return variant;
    }

    [Fact]
    public async Task InventoryCheckAndRowversionRejectOversellAndStaleWriter()
    {
        var variant = await AddVariant();
        await using var first = fixture.CreateContext();
        first.Inventory.Add(new Inventory { VariantId = variant.Id, OnHand = 1 });
        await first.SaveChangesAsync();
        await using var stale = fixture.CreateContext();
        var a = await first.Inventory.SingleAsync(x => x.VariantId == variant.Id);
        var b = await stale.Inventory.SingleAsync(x => x.VariantId == variant.Id);
        a.Reserved = 1;
        await first.SaveChangesAsync();
        b.Reserved = 1;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var invalid = fixture.CreateContext();
        var row = await invalid.Inventory.SingleAsync(x => x.VariantId == variant.Id);
        row.Reserved = 2;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        await using var verify = fixture.CreateContext();
        var persisted = await verify.Inventory.SingleAsync(x => x.VariantId == variant.Id);
        Assert.Equal(1, persisted.OnHand);
        Assert.Equal(1, persisted.Reserved);
    }

    [Fact]
    public async Task ImageCannotReferenceVariantOfAnotherProduct()
    {
        var one = await AddVariant();
        var two = await AddVariant();
        await using var db = fixture.CreateContext();
        db.ProductImages.Add(new ProductImage { ProductId = one.ProductId, VariantId = two.Id, ImageUrl = "/synthetic.png", AltText = "fixture" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Fact]
    public async Task FilteredIndexAllowsSeveralNondefaultsButOnlyOneDefaultAddress()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "fixture-" + Guid.NewGuid() + "@example.invalid", FullName = "Fixture" };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        await using var db = fixture.CreateContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        Address Create(bool isDefault) => new() { UserId = user.Id, RecipientName = "Fixture", Phone = "000", AddressLine = "Fixture", Province = "Fixture", CountryCode = "VN", IsDefault = isDefault };
        db.Addresses.AddRange(Create(false), Create(false), Create(true));
        await db.SaveChangesAsync();
        db.Addresses.Add(Create(true));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task IdentityUpgradePreservesDataAndRejectsOversizedKeys()
    {
        // Isolated second DB for an old-schema upgrade; lifecycle uses the same guarded fixture.
        var old = new SqlFixture();
        await old.InitializeAtAsync("20261006173535_InitialCreate");
        try
        {
            await using var db = old.CreateContext();

            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT AspNetUsers(Id,Email,NormalizedEmail,FullName,IsActive,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount) VALUES({id},'fixture@example.invalid','FIXTURE@EXAMPLE.INVALID','Fixture',1,0,0,0,0,0)");
            var oversized = new string('p', 129);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT AspNetUserLogins(LoginProvider,ProviderKey,ProviderDisplayName,UserId) VALUES({oversized},'key','fixture',{id})");
            var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());
            Assert.Equal(51000, error.Number);
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
            await db.Database.ExecuteSqlRawAsync("UPDATE AspNetUserLogins SET LoginProvider='fixture-provider'");
            await db.Database.MigrateAsync();
            Assert.Equal("key", await db.Set<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().Select(x => x.ProviderKey).SingleAsync());
            Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        }
        finally { await old.DisposeAsync(); }
    }
    [Fact]
    public async Task OrderNotes_upgrade_preserves_existing_order_and_has_fk_check_and_unique_constraints()
    {
        var old = new SqlFixture(); await old.InitializeAtAsync("20261007130024_BoundIdentityKeys");
        try {
            await using var db = old.CreateContext(); var key = Guid.NewGuid(); var user = new ApplicationUser { Id = key, FullName = "Synthetic upgrade", Email = key + "@example.invalid", NormalizedEmail = key.ToString().ToUpperInvariant() + "@EXAMPLE.INVALID" }; db.Users.Add(user);
            var order = new Order { OrderNumber = "PS" + key.ToString("N")[..28], UserId = key, CustomerEmail = user.Email, NormalizedCustomerEmail = user.NormalizedEmail, RecipientName = "Snapshot", Phone = "0000000000", AddressLine = "Snapshot street", Province = "Snapshot province", Subtotal = 100, GrandTotal = 100, CreatedAt = DateTime.UtcNow }; db.Orders.Add(order); await db.SaveChangesAsync(); var version = order.Version.ToArray();
            await db.Database.MigrateAsync(); await db.Database.MigrateAsync(); var stored = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id); Assert.Equal(100, stored.GrandTotal); Assert.Equal("Snapshot street", stored.AddressLine); Assert.Equal(version, stored.Version);
            var note = new OrderInternalNote { OrderId = order.Id, ActorUserId = key, OperationKey = Guid.NewGuid(), Text = "Synthetic note", CreatedAt = DateTime.UtcNow }; db.OrderInternalNotes.Add(note); await db.SaveChangesAsync();
            await using (var duplicate = old.CreateContext()) { duplicate.OrderInternalNotes.Add(new OrderInternalNote { OrderId = order.Id, ActorUserId = key, OperationKey = note.OperationKey, Text = "Duplicate" }); var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync()); Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 }); }
            await using (var invalid = old.CreateContext()) { invalid.OrderInternalNotes.Add(new OrderInternalNote { OrderId = order.Id, ActorUserId = key, OperationKey = Guid.NewGuid(), Text = " " }); Assert.Equal(547, Assert.IsType<SqlException>((await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync())).InnerException).Number); }
            await using (var invalid = old.CreateContext()) { invalid.OrderInternalNotes.Add(new OrderInternalNote { OrderId = long.MaxValue, ActorUserId = key, OperationKey = Guid.NewGuid(), Text = "Missing order" }); Assert.Equal(547, Assert.IsType<SqlException>((await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync())).InnerException).Number); }
            Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        } finally { await old.DisposeAsync(); }
    }

}

