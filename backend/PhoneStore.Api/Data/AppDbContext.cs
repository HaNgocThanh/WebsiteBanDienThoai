using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Data;

/// <summary>
/// SQL Server schema from docs/01 and docs/03. Cross-row business rules belong
/// in services and transactions, not in entity setters or SaveChanges.
/// Tier thresholds must be configured before membership is enabled.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    // SQL datetime2 has no timezone. UTC kind is restored when reading values.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<CustomerTier> CustomerTiers => Set<CustomerTier>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerSpendEntry> CustomerSpendEntries => Set<CustomerSpendEntry>();
    public DbSet<CustomerTierHistory> CustomerTierHistories => Set<CustomerTierHistory>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionProduct> PromotionProducts => Set<PromotionProduct>();
    public DbSet<PromotionCustomerTier> PromotionCustomerTiers => Set<PromotionCustomerTier>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<OrderCancellationRequest> OrderCancellationRequests => Set<OrderCancellationRequest>();
    public DbSet<OrderAccountConsent> OrderAccountConsents => Set<OrderAccountConsent>();
    public DbSet<OrderAccessToken> OrderAccessTokens => Set<OrderAccessToken>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyOption> SurveyOptions => Set<SurveyOption>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<SurveyAnswer> SurveyAnswers => Set<SurveyAnswer>();
    public DbSet<SurveyAnswerOption> SurveyAnswerOptions => Set<SurveyAnswerOption>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRequest> IdempotencyRequests => Set<IdempotencyRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Bound composite Identity keys below SQL Server's 900-byte clustered key limit.
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserLogin<Guid>>().Property(x => x.ProviderKey).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        modelBuilder.Entity<IdentityUserToken<Guid>>().Property(x => x.Name).HasMaxLength(128);

        ConfigureApplicationUser(modelBuilder.Entity<ApplicationUser>());
        ConfigureAddress(modelBuilder.Entity<Address>());
        ConfigureCustomerTier(modelBuilder.Entity<CustomerTier>());
        ConfigureCustomerProfile(modelBuilder.Entity<CustomerProfile>());
        ConfigureCustomerSpendEntry(modelBuilder.Entity<CustomerSpendEntry>());
        ConfigureCustomerTierHistory(modelBuilder.Entity<CustomerTierHistory>());
        ConfigureBrand(modelBuilder.Entity<Brand>());
        ConfigureCategory(modelBuilder.Entity<Category>());
        ConfigureProduct(modelBuilder.Entity<Product>());
        ConfigureProductVariant(modelBuilder.Entity<ProductVariant>());
        ConfigureProductImage(modelBuilder.Entity<ProductImage>());
        ConfigureInventory(modelBuilder.Entity<Inventory>());
        ConfigureStockReservation(modelBuilder.Entity<StockReservation>());
        ConfigureInventoryMovement(modelBuilder.Entity<InventoryMovement>());
        ConfigurePromotion(modelBuilder.Entity<Promotion>());
        ConfigurePromotionProduct(modelBuilder.Entity<PromotionProduct>());
        ConfigurePromotionCustomerTier(modelBuilder.Entity<PromotionCustomerTier>());
        ConfigureOrder(modelBuilder.Entity<Order>());
        ConfigureOrderItem(modelBuilder.Entity<OrderItem>());
        ConfigureOrderStatusHistory(modelBuilder.Entity<OrderStatusHistory>());
        ConfigureOrderCancellationRequest(modelBuilder.Entity<OrderCancellationRequest>());
        ConfigureOrderAccountConsent(modelBuilder.Entity<OrderAccountConsent>());
        ConfigureOrderAccessToken(modelBuilder.Entity<OrderAccessToken>());
        ConfigurePayment(modelBuilder.Entity<Payment>());
        ConfigureRefund(modelBuilder.Entity<Refund>());
        ConfigureReview(modelBuilder.Entity<Review>());
        ConfigureSurvey(modelBuilder.Entity<Survey>());
        ConfigureSurveyQuestion(modelBuilder.Entity<SurveyQuestion>());
        ConfigureSurveyOption(modelBuilder.Entity<SurveyOption>());
        ConfigureSurveyResponse(modelBuilder.Entity<SurveyResponse>());
        ConfigureSurveyAnswer(modelBuilder.Entity<SurveyAnswer>());
        ConfigureSurveyAnswerOption(modelBuilder.Entity<SurveyAnswerOption>());
        ConfigureNotification(modelBuilder.Entity<Notification>());
        ConfigureOutboxMessage(modelBuilder.Entity<OutboxMessage>());
        ConfigureIdempotencyRequest(modelBuilder.Entity<IdempotencyRequest>());
        ConfigureAuditLog(modelBuilder.Entity<AuditLog>());

        // Includes standard Identity relationships; historical data never cascades.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetUpdatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
                entry.Property("UpdatedAt").CurrentValue = now;
        }
    }

    private static void ConfigureApplicationUser(EntityTypeBuilder<ApplicationUser> entity)
    {
        entity.ToTable("AspNetUsers");
        entity.Property(x => x.FullName).HasMaxLength(150).IsUnicode().IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
        entity.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
    }

    private static void ConfigureAddress(EntityTypeBuilder<Address> entity)
    {
        entity.ToTable("Addresses");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.RecipientName).HasMaxLength(150).IsUnicode().IsRequired(true);
        entity.Property(x => x.Phone).HasMaxLength(30).IsUnicode().IsRequired(true);
        entity.Property(x => x.AddressLine).HasMaxLength(300).IsUnicode().IsRequired(true);
        entity.Property(x => x.Locality).HasMaxLength(150).IsUnicode().IsRequired(false);
        entity.Property(x => x.Province).HasMaxLength(150).IsUnicode().IsRequired(true);
        entity.Property(x => x.CountryCode).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.HasIndex(x => x.UserId).IsUnique().HasFilter("[IsDefault] = 1");
        entity.Property(x => x.CountryCode).HasDefaultValue("VN");
        entity.HasOne(x => x.User).WithMany(x => x.Addresses)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureCustomerTier(EntityTypeBuilder<CustomerTier> entity)
    {
        entity.ToTable("CustomerTiers", table =>
        {
            table.HasCheckConstraint("CK_CustomerTiers_MinimumSpend", "[MinimumSpend] >= 0");
            table.HasCheckConstraint("CK_CustomerTiers_Rank", "[Rank] > 0");
            table.HasCheckConstraint("CK_CustomerTiers_Code", "[Code] IN ('Bronze', 'Silver', 'Gold', 'Diamond')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Code).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.Name).HasMaxLength(50).IsUnicode().IsRequired(true);
        entity.Property(x => x.MinimumSpend).HasPrecision(18, 0);
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasIndex(x => x.MinimumSpend).IsUnique();
        entity.HasIndex(x => x.Rank).IsUnique();
    }

    private static void ConfigureCustomerProfile(EntityTypeBuilder<CustomerProfile> entity)
    {
        entity.ToTable("CustomerProfiles", table =>
        {
            table.HasCheckConstraint("CK_CustomerProfiles_EligibleSpend", "[EligibleSpend] >= 0");
        });
        entity.HasKey(x => x.UserId);
        entity.Property(x => x.EligibleSpend).HasPrecision(18, 0);
        entity.Property(x => x.TierCalculatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasOne(x => x.User).WithOne(x => x.CustomerProfile)
            .HasForeignKey<CustomerProfile>(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Tier).WithMany(x => x.CustomerProfiles)
            .HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureCustomerSpendEntry(EntityTypeBuilder<CustomerSpendEntry> entity)
    {
        entity.ToTable("CustomerSpendEntries", table =>
        {
            table.HasCheckConstraint("CK_CustomerSpendEntries_KindAmountRefund", "([Kind] = 'OrderCompleted' AND [Amount] >= 0 AND [RefundId] IS NULL) OR ([Kind] = 'Refund' AND [Amount] < 0 AND [RefundId] IS NOT NULL)");
            table.HasCheckConstraint("CK_CustomerSpendEntries_Kind", "[Kind] IN ('OrderCompleted', 'Refund')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.Amount).HasPrecision(18, 0);
        entity.Property(x => x.EventKey).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.EventKey).IsUnique();
        entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        entity.HasOne(x => x.User).WithMany(x => x.SpendEntries)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Order).WithMany(x => x.SpendEntries)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Refund).WithMany()
            .HasForeignKey(x => x.RefundId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureCustomerTierHistory(EntityTypeBuilder<CustomerTierHistory> entity)
    {
        entity.ToTable("CustomerTierHistories");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.EligibleSpend).HasPrecision(18, 0);
        entity.Property(x => x.Reason).HasMaxLength(300).IsUnicode().IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        entity.HasOne(x => x.User).WithMany(x => x.TierHistories)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.PreviousTier).WithMany()
            .HasForeignKey(x => x.PreviousTierId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.NewTier).WithMany()
            .HasForeignKey(x => x.NewTierId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureBrand(EntityTypeBuilder<Brand> entity)
    {
        entity.ToTable("Brands");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Name).HasMaxLength(100).IsUnicode().IsRequired(true);
        entity.Property(x => x.Slug).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.Name).IsUnique();
        entity.HasIndex(x => x.Slug).IsUnique();
    }

    private static void ConfigureCategory(EntityTypeBuilder<Category> entity)
    {
        entity.ToTable("Categories");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Name).HasMaxLength(100).IsUnicode().IsRequired(true);
        entity.Property(x => x.Slug).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => x.Slug).IsUnique();
    }

    private static void ConfigureProduct(EntityTypeBuilder<Product> entity)
    {
        entity.ToTable("Products", table =>
        {
            table.HasCheckConstraint("CK_Products_SpecificationsJson", "[SpecificationsJson] IS NULL OR ISJSON([SpecificationsJson]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Name).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.Slug).HasMaxLength(220).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired(true);
        entity.Property(x => x.SpecificationsJson).HasColumnType("nvarchar(max)").IsRequired(false);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.Slug).IsUnique();
        entity.HasIndex(x => new { x.BrandId, x.CategoryId, x.IsActive });
        entity.HasOne(x => x.Brand).WithMany(x => x.Products)
            .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureProductVariant(EntityTypeBuilder<ProductVariant> entity)
    {
        entity.ToTable("ProductVariants", table =>
        {
            table.HasCheckConstraint("CK_ProductVariants_StorageGb", "[StorageGb] > 0");
            table.HasCheckConstraint("CK_ProductVariants_RamGb", "[RamGb] > 0");
            table.HasCheckConstraint("CK_ProductVariants_Price", "[Price] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Sku).HasMaxLength(64).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.Color).HasMaxLength(50).IsUnicode().IsRequired(true);
        entity.Property(x => x.Price).HasPrecision(18, 0);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.Sku).IsUnique();
        entity.HasIndex(x => new { x.ProductId, x.Color, x.StorageGb, x.RamGb }).IsUnique();
        entity.HasIndex(x => new { x.ProductId, x.IsActive, x.Price });
        entity.HasAlternateKey(x => new { x.ProductId, x.Id });
        entity.HasOne(x => x.Product).WithMany(x => x.Variants)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureProductImage(EntityTypeBuilder<ProductImage> entity)
    {
        entity.ToTable("ProductImages", table =>
        {
            table.HasCheckConstraint("CK_ProductImages_SortOrder", "[SortOrder] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.ImageUrl).HasMaxLength(1000).IsUnicode().IsRequired(true);
        entity.Property(x => x.AltText).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.HasOne(x => x.Product).WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Variant).WithMany(x => x.Images)
            .HasForeignKey(x => new { x.ProductId, x.VariantId })
            .HasPrincipalKey(x => new { x.ProductId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureInventory(EntityTypeBuilder<Inventory> entity)
    {
        entity.ToTable("Inventory", table =>
        {
            table.HasCheckConstraint("CK_Inventory_Balances", "[OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand]");
        });
        entity.HasKey(x => x.VariantId);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.Ignore(x => x.Available);
        entity.HasOne(x => x.Variant).WithOne(x => x.Inventory)
            .HasForeignKey<Inventory>(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureStockReservation(EntityTypeBuilder<StockReservation> entity)
    {
        entity.ToTable("StockReservations", table =>
        {
            table.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockReservations_Status", "[Status] IN ('Active', 'Consumed', 'Released')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.ExpiresAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ClosedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => new { x.Status, x.ExpiresAt });
        entity.HasOne(x => x.OrderItem).WithOne(x => x.StockReservation)
            .HasForeignKey<StockReservation>(x => x.OrderItemId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureInventoryMovement(EntityTypeBuilder<InventoryMovement> entity)
    {
        entity.ToTable("InventoryMovements", table =>
        {
            table.HasCheckConstraint("CK_InventoryMovements_Kind", "[Kind] IN ('Receive', 'Adjust', 'Reserve', 'Release', 'Dispatch', 'CancelReturn')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24).IsUnicode(false);
        entity.Property(x => x.Reason).HasMaxLength(300).IsUnicode().IsRequired(true);
        entity.Property(x => x.EventKey).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.RequestHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(false);
        entity.HasIndex(x => x.EventKey).IsUnique();
        entity.HasIndex(x => new { x.VariantId, x.CreatedAt });
        entity.HasOne(x => x.Variant).WithMany(x => x.InventoryMovements)
            .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.OrderItem).WithMany(x => x.InventoryMovements)
            .HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigurePromotion(EntityTypeBuilder<Promotion> entity)
    {
        entity.ToTable("Promotions", table =>
        {
            table.HasCheckConstraint("CK_Promotions_Dates", "[StartsAt] < [EndsAt]");
            table.HasCheckConstraint("CK_Promotions_DiscountValue", "[DiscountValue] > 0 AND ([DiscountType] <> 'Percent' OR [DiscountValue] <= 100)");
            table.HasCheckConstraint("CK_Promotions_MaxDiscountPerUnit", "[MaxDiscountPerUnit] IS NULL OR [MaxDiscountPerUnit] > 0");
            table.HasCheckConstraint("CK_Promotions_Status", "[Status] IN ('Draft', 'Published', 'Disabled')");
            table.HasCheckConstraint("CK_Promotions_ProductScope", "[ProductScope] IN ('All', 'Selected')");
            table.HasCheckConstraint("CK_Promotions_AudienceScope", "[AudienceScope] IN ('All', 'SelectedTiers')");
            table.HasCheckConstraint("CK_Promotions_DiscountType", "[DiscountType] IN ('Percent', 'Fixed')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Name).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.Description).HasMaxLength(1000).IsUnicode().IsRequired(false);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.ProductScope).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.AudienceScope).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.DiscountValue).HasPrecision(18, 2);
        entity.Property(x => x.MaxDiscountPerUnit).HasPrecision(18, 0);
        entity.Property(x => x.StartsAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.EndsAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => new { x.Status, x.StartsAt, x.EndsAt });
    }

    private static void ConfigurePromotionProduct(EntityTypeBuilder<PromotionProduct> entity)
    {
        entity.ToTable("PromotionProducts");
        entity.HasKey(x => new { x.PromotionId, x.ProductId });
        entity.HasIndex(x => new { x.ProductId, x.PromotionId });
        entity.HasOne(x => x.Promotion).WithMany(x => x.Products)
            .HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Product).WithMany(x => x.Promotions)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigurePromotionCustomerTier(EntityTypeBuilder<PromotionCustomerTier> entity)
    {
        entity.ToTable("PromotionCustomerTiers");
        entity.HasKey(x => new { x.PromotionId, x.TierId });
        entity.HasIndex(x => new { x.TierId, x.PromotionId });
        entity.HasOne(x => x.Promotion).WithMany(x => x.CustomerTiers)
            .HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Tier).WithMany(x => x.Promotions)
            .HasForeignKey(x => x.TierId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrder(EntityTypeBuilder<Order> entity)
    {
        entity.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Currency", "[Currency] = 'VND'");
            table.HasCheckConstraint("CK_Orders_Totals", "[Subtotal] >= 0 AND [DiscountTotal] >= 0 AND [ShippingFee] >= 0 AND [GrandTotal] >= 0 AND [DiscountTotal] <= [Subtotal] AND [GrandTotal] = [Subtotal] - [DiscountTotal] + [ShippingFee]");
            table.HasCheckConstraint("CK_Orders_Status", "[Status] IN ('Placed', 'Reviewing', 'Approved', 'Shipping', 'Delivered', 'Completed', 'Cancelled')");
            table.HasCheckConstraint("CK_Orders_PaymentMethod", "[PaymentMethod] IN ('COD', 'BankTransfer')");
            table.HasCheckConstraint("CK_Orders_TierCodeSnapshot", "[TierCodeSnapshot] IS NULL OR [TierCodeSnapshot] IN ('Bronze', 'Silver', 'Gold', 'Diamond')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.OrderNumber).HasMaxLength(30).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CustomerEmail).HasMaxLength(256).IsUnicode().IsRequired(true);
        entity.Property(x => x.NormalizedCustomerEmail).HasMaxLength(256).IsUnicode().IsRequired(true);
        entity.Property(x => x.RecipientName).HasMaxLength(150).IsUnicode().IsRequired(true);
        entity.Property(x => x.Phone).HasMaxLength(30).IsUnicode().IsRequired(true);
        entity.Property(x => x.AddressLine).HasMaxLength(300).IsUnicode().IsRequired(true);
        entity.Property(x => x.Locality).HasMaxLength(150).IsUnicode().IsRequired(false);
        entity.Property(x => x.Province).HasMaxLength(150).IsUnicode().IsRequired(true);
        entity.Property(x => x.CountryCode).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired(true);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired(true);
        entity.Property(x => x.Subtotal).HasPrecision(18, 0);
        entity.Property(x => x.DiscountTotal).HasPrecision(18, 0);
        entity.Property(x => x.ShippingFee).HasPrecision(18, 0);
        entity.Property(x => x.GrandTotal).HasPrecision(18, 0);
        entity.Property(x => x.TierCodeSnapshot).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.CustomerNote).HasMaxLength(1000).IsUnicode().IsRequired(false);
        entity.Property(x => x.PaymentDueAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.ShippedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.DeliveredAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CompletedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CancelledAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.OrderNumber).IsUnique();
        entity.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true);
        entity.HasIndex(x => new { x.Status, x.CreatedAt }).IsDescending(false, true);
        entity.HasIndex(x => x.CreatedAt).IsDescending(true);
        entity.HasIndex(x => new { x.NormalizedCustomerEmail, x.CreatedAt }).IsDescending(false, true);
        entity.HasIndex(x => new { x.Status, x.PaymentDueAt });
        entity.HasOne(x => x.User).WithMany(x => x.Orders)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.TierAtPurchase).WithMany()
            .HasForeignKey(x => x.TierIdAtPurchase).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrderItem(EntityTypeBuilder<OrderItem> entity)
    {
        entity.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_OrderItems_Prices", "[UnitPrice] >= 0 AND [UnitDiscount] >= 0 AND [UnitDiscount] <= [UnitPrice]");
            table.HasCheckConstraint("CK_OrderItems_PromotionRuleSnapshot", "[PromotionRuleSnapshot] IS NULL OR ISJSON([PromotionRuleSnapshot]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.ProductNameSnapshot).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.SkuSnapshot).HasMaxLength(64).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.VariantSnapshot).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.UnitPrice).HasPrecision(18, 0);
        entity.Property(x => x.UnitDiscount).HasPrecision(18, 0);
        entity.Property(x => x.LineTotal).HasPrecision(18, 0);
        entity.Property(x => x.PromotionNameSnapshot).HasMaxLength(200).IsUnicode().IsRequired(false);
        entity.Property(x => x.PromotionRuleSnapshot).HasColumnType("nvarchar(max)").IsRequired(false);
        entity.HasIndex(x => new { x.OrderId, x.VariantId }).IsUnique();
        entity.Property(x => x.LineTotal).HasComputedColumnSql("CONVERT(decimal(18,0), [Quantity] * ([UnitPrice] - [UnitDiscount]))", stored: true);
        entity.HasOne(x => x.Order).WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Variant).WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Promotion).WithMany()
            .HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrderStatusHistory(EntityTypeBuilder<OrderStatusHistory> entity)
    {
        entity.ToTable("OrderStatusHistories", table =>
        {
            table.HasCheckConstraint("CK_OrderStatusHistories_FromStatus", "[FromStatus] IS NULL OR [FromStatus] IN ('Placed', 'Reviewing', 'Approved', 'Shipping', 'Delivered', 'Completed', 'Cancelled')");
            table.HasCheckConstraint("CK_OrderStatusHistories_ToStatus", "[ToStatus] IN ('Placed', 'Reviewing', 'Approved', 'Shipping', 'Delivered', 'Completed', 'Cancelled')");
            table.HasCheckConstraint("CK_OrderStatusHistories_ActorType", "[ActorType] IN ('Customer', 'Admin', 'System')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.Reason).HasMaxLength(500).IsUnicode().IsRequired(false);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.OrderId, x.CreatedAt });
        entity.HasOne(x => x.Order).WithMany(x => x.StatusHistories)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrderCancellationRequest(EntityTypeBuilder<OrderCancellationRequest> entity)
    {
        entity.ToTable("OrderCancellationRequests", table =>
        {
            table.HasCheckConstraint("CK_OrderCancellationRequests_Status", "[Status] IN ('Pending', 'Approved', 'Rejected')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Reason).HasMaxLength(500).IsUnicode().IsRequired(true);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.ReviewNote).HasMaxLength(500).IsUnicode().IsRequired(false);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ReviewedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.OrderId).IsUnique().HasFilter("[Status] = 'Pending'");
        entity.HasOne(x => x.Order).WithMany(x => x.CancellationRequests)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.RequestedByUser).WithMany()
            .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.ReviewedByUser).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrderAccountConsent(EntityTypeBuilder<OrderAccountConsent> entity)
    {
        entity.ToTable("OrderAccountConsents");
        entity.HasKey(x => x.OrderId);
        entity.Property(x => x.ConsentTextVersion).HasMaxLength(30).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.RecordedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.HasOne(x => x.Order).WithOne(x => x.AccountConsent)
            .HasForeignKey<OrderAccountConsent>(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOrderAccessToken(EntityTypeBuilder<OrderAccessToken> entity)
    {
        entity.ToTable("OrderAccessTokens", table =>
        {
            table.HasCheckConstraint("CK_OrderAccessTokens_Purpose", "[Purpose] IN ('ViewOrder', 'ClaimOrder', 'CancelOrder')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.TokenHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(true);
        entity.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        entity.Property(x => x.ExpiresAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.UsedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.TokenHash).IsUnique();
        entity.HasOne(x => x.Order).WithMany(x => x.AccessTokens)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigurePayment(EntityTypeBuilder<Payment> entity)
    {
        entity.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Payments_Method", "[Method] IN ('COD', 'BankTransfer')");
            table.HasCheckConstraint("CK_Payments_Status", "[Status] IN ('Pending', 'Confirmed', 'Rejected')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.Amount).HasPrecision(18, 0);
        entity.Property(x => x.Reference).HasMaxLength(150).IsUnicode(false).IsRequired(false);
        entity.Property(x => x.ProofUrl).HasMaxLength(1000).IsUnicode().IsRequired(false);
        entity.Property(x => x.Note).HasMaxLength(500).IsUnicode().IsRequired(false);
        entity.Property(x => x.ConfirmedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.Property(x => x.OperationKey).HasMaxLength(150).IsUnicode(false).IsRequired(false);
        entity.Property(x => x.RequestHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(false);
        entity.HasIndex(x => x.OperationKey).IsUnique().HasFilter("[OperationKey] IS NOT NULL");
        entity.HasIndex(x => new { x.Method, x.Reference }).IsUnique().HasFilter("[Reference] IS NOT NULL");
        entity.HasIndex(x => new { x.OrderId, x.Status });
        entity.HasOne(x => x.Order).WithMany(x => x.Payments)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.ConfirmedByUser).WithMany()
            .HasForeignKey(x => x.ConfirmedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureRefund(EntityTypeBuilder<Refund> entity)
    {
        entity.ToTable("Refunds", table =>
        {
            table.HasCheckConstraint("CK_Refunds_Amounts", "[MerchandiseAmount] >= 0 AND [ShippingAmount] >= 0 AND [MerchandiseAmount] + [ShippingAmount] > 0");
            table.HasCheckConstraint("CK_Refunds_Status", "[Status] IN ('Pending', 'Completed', 'Failed')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.MerchandiseAmount).HasPrecision(18, 0);
        entity.Property(x => x.ShippingAmount).HasPrecision(18, 0);
        entity.Property(x => x.Amount).HasPrecision(18, 0);
        entity.Property(x => x.Reason).HasMaxLength(500).IsUnicode().IsRequired(true);
        entity.Property(x => x.Reference).HasMaxLength(150).IsUnicode(false).IsRequired(false);
        entity.Property(x => x.EventKey).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.RequestHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.CompletedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.EventKey).IsUnique();
        entity.HasIndex(x => new { x.PaymentId, x.Status });
        entity.Property(x => x.Amount).HasComputedColumnSql("CONVERT(decimal(18,0), [MerchandiseAmount] + [ShippingAmount])", stored: true);
        entity.HasOne(x => x.Payment).WithMany(x => x.Refunds)
            .HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.CompletedByUser).WithMany()
            .HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureReview(EntityTypeBuilder<Review> entity)
    {
        entity.ToTable("Reviews", table =>
        {
            table.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_Reviews_Status", "[Status] IN ('Visible', 'Hidden')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Content).HasMaxLength(2000).IsUnicode().IsRequired(true);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.ModerationReason).HasMaxLength(500).IsUnicode().IsRequired(false);
        entity.Property(x => x.ModeratedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.AdminReply).HasMaxLength(2000).IsUnicode().IsRequired(false);
        entity.Property(x => x.RepliedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => new { x.UserId, x.CreatedAt });
        entity.HasOne(x => x.OrderItem).WithOne(x => x.Review)
            .HasForeignKey<Review>(x => x.OrderItemId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.User).WithMany(x => x.Reviews)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.ModeratedByUser).WithMany()
            .HasForeignKey(x => x.ModeratedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.RepliedByUser).WithMany()
            .HasForeignKey(x => x.RepliedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurvey(EntityTypeBuilder<Survey> entity)
    {
        entity.ToTable("Surveys", table =>
        {
            table.HasCheckConstraint("CK_Surveys_Dates", "[StartsAt] < [EndsAt]");
            table.HasCheckConstraint("CK_Surveys_Status", "[Status] IN ('Draft', 'Published', 'Closed')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Title).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.Description).HasMaxLength(2000).IsUnicode().IsRequired(false);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.StartsAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.EndsAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurveyQuestion(EntityTypeBuilder<SurveyQuestion> entity)
    {
        entity.ToTable("SurveyQuestions", table =>
        {
            table.HasCheckConstraint("CK_SurveyQuestions_SortOrder", "[SortOrder] >= 0");
            table.HasCheckConstraint("CK_SurveyQuestions_Type", "[Type] IN ('SingleChoice', 'MultipleChoice', 'Text')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Text).HasMaxLength(1000).IsUnicode().IsRequired(true);
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.HasIndex(x => new { x.SurveyId, x.SortOrder }).IsUnique();
        entity.HasAlternateKey(x => new { x.SurveyId, x.Id });
        entity.HasOne(x => x.Survey).WithMany(x => x.Questions)
            .HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurveyOption(EntityTypeBuilder<SurveyOption> entity)
    {
        entity.ToTable("SurveyOptions", table =>
        {
            table.HasCheckConstraint("CK_SurveyOptions_SortOrder", "[SortOrder] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Text).HasMaxLength(500).IsUnicode().IsRequired(true);
        entity.HasIndex(x => new { x.QuestionId, x.SortOrder }).IsUnique();
        entity.HasAlternateKey(x => new { x.QuestionId, x.Id });
        entity.HasOne(x => x.Question).WithMany(x => x.Options)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurveyResponse(EntityTypeBuilder<SurveyResponse> entity)
    {
        entity.ToTable("SurveyResponses");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.SubmittedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.HasIndex(x => new { x.SurveyId, x.UserId }).IsUnique();
        entity.HasAlternateKey(x => new { x.SurveyId, x.Id });
        entity.HasOne(x => x.Survey).WithMany(x => x.Responses)
            .HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.User).WithMany(x => x.SurveyResponses)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurveyAnswer(EntityTypeBuilder<SurveyAnswer> entity)
    {
        entity.ToTable("SurveyAnswers");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.TextValue).HasMaxLength(4000).IsUnicode().IsRequired(false);
        entity.HasIndex(x => new { x.ResponseId, x.QuestionId }).IsUnique();
        entity.HasAlternateKey(x => new { x.QuestionId, x.Id });
        entity.HasOne(x => x.Response).WithMany(x => x.Answers)
            .HasForeignKey(x => new { x.SurveyId, x.ResponseId })
            .HasPrincipalKey(x => new { x.SurveyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Question).WithMany(x => x.Answers)
            .HasForeignKey(x => new { x.SurveyId, x.QuestionId })
            .HasPrincipalKey(x => new { x.SurveyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureSurveyAnswerOption(EntityTypeBuilder<SurveyAnswerOption> entity)
    {
        entity.ToTable("SurveyAnswerOptions");
        entity.HasKey(x => new { x.AnswerId, x.OptionId });
        entity.HasOne(x => x.Answer).WithMany(x => x.SelectedOptions)
            .HasForeignKey(x => new { x.QuestionId, x.AnswerId })
            .HasPrincipalKey(x => new { x.QuestionId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Option).WithMany(x => x.Answers)
            .HasForeignKey(x => new { x.QuestionId, x.OptionId })
            .HasPrincipalKey(x => new { x.QuestionId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureNotification(EntityTypeBuilder<Notification> entity)
    {
        entity.ToTable("Notifications");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Type).HasMaxLength(50).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.Title).HasMaxLength(200).IsUnicode().IsRequired(true);
        entity.Property(x => x.Body).HasMaxLength(1000).IsUnicode().IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ReadAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.EventKey).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.HasIndex(x => new { x.UserId, x.EventKey }).IsUnique();
        entity.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        entity.HasOne(x => x.User).WithMany(x => x.Notifications)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Order).WithMany(x => x.Notifications)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureOutboxMessage(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_PayloadJson", "ISJSON([PayloadJson]) = 1");
            table.HasCheckConstraint("CK_OutboxMessages_Attempts", "[Attempts] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.EventKey).HasMaxLength(150).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.Type).HasMaxLength(100).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ProcessedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.NextAttemptAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.LockedUntil).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.LockOwner).HasMaxLength(100).IsUnicode(false).IsRequired(false);
        entity.Property(x => x.LastError).HasMaxLength(2000).IsUnicode().IsRequired(false);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.EventKey).IsUnique();
        entity.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
    }

    private static void ConfigureIdempotencyRequest(EntityTypeBuilder<IdempotencyRequest> entity)
    {
        entity.ToTable("IdempotencyRequests", table =>
        {
            table.HasCheckConstraint("CK_IdempotencyRequests_Owner", "([OwnerUserId] IS NOT NULL AND [GuestSessionHash] IS NULL) OR ([OwnerUserId] IS NULL AND [GuestSessionHash] IS NOT NULL)");
            table.HasCheckConstraint("CK_IdempotencyRequests_Status", "[Status] IN ('Issued', 'Completed')");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.GuestSessionHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(false);
        entity.Property(x => x.RequestHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired(false);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(x => x.ExpiresAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter);
        entity.Property(x => x.Version).IsRowVersion().IsRequired(true);
        entity.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL");
        entity.HasOne(x => x.OwnerUser).WithMany()
            .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.Order).WithOne(x => x.IdempotencyRequest)
            .HasForeignKey<IdempotencyRequest>(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureAuditLog(EntityTypeBuilder<AuditLog> entity)
    {
        entity.ToTable("AuditLogs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).UseIdentityColumn();
        entity.Property(x => x.Action).HasMaxLength(100).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.EntityType).HasMaxLength(100).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.EntityId).HasMaxLength(100).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.Summary).HasMaxLength(2000).IsUnicode().IsRequired(true);
        entity.Property(x => x.CorrelationId).HasMaxLength(100).IsUnicode(false).IsRequired(true);
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasConversion(UtcConverter).HasDefaultValueSql("SYSUTCDATETIME()");
        entity.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
        entity.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}


