SET NOCOUNT ON;
IF DB_NAME() <> 'PhoneStore_Test_P0Schema' THROW 51000, 'Wrong test database', 1;
IF NOT EXISTS(SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId='20261006173535_InitialCreate') THROW 51000, 'Missing migration', 1;
IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE delete_referential_action<>0) THROW 51000, 'Unexpected cascade', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('Addresses') AND is_unique=1 AND has_filter=1) THROW 51000, 'Missing filtered index', 1;
BEGIN TRANSACTION;
INSERT Brands(Name,Slug,IsActive) VALUES('P0 fixture','p0-fixture',1);
DECLARE @b bigint=SCOPE_IDENTITY();
INSERT Categories(Name,Slug,IsActive) VALUES('P0 fixture','p0-fixture',1);
DECLARE @c bigint=SCOPE_IDENTITY();
INSERT Products(BrandId,CategoryId,Name,Slug,Description,IsActive) VALUES(@b,@c,'P0 fixture','p0-fixture','synthetic',1);
DECLARE @p bigint=SCOPE_IDENTITY();
INSERT ProductVariants(ProductId,Sku,Color,StorageGb,RamGb,Price,IsActive) VALUES(@p,'P0-SKU','Black',128,8,1000000,1);
DECLARE @v bigint=SCOPE_IDENTITY();
INSERT Inventory(VariantId,OnHand,Reserved) VALUES(@v,1,0);
DECLARE @rv binary(8)=(SELECT Version FROM Inventory WHERE VariantId=@v);
UPDATE Inventory SET Reserved=1 WHERE VariantId=@v;
IF @rv=(SELECT Version FROM Inventory WHERE VariantId=@v) THROW 51000, 'rowversion unchanged', 1;
BEGIN TRY
 UPDATE Inventory SET Reserved=2 WHERE VariantId=@v;
 THROW 51000, 'Inventory CHECK accepted oversell', 1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>547 THROW;
END CATCH;
BEGIN TRY
 INSERT ProductVariants(ProductId,Sku,Color,StorageGb,RamGb,Price,IsActive) VALUES(@p,'P0-SKU','White',256,8,1000000,1);
 THROW 51000, 'SKU uniqueness accepted duplicate', 1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
END CATCH;
BEGIN TRY
 INSERT ProductImages(ProductId,VariantId,ImageUrl,AltText,SortOrder) VALUES(@p,@v+999999,'/fixture.png','synthetic',0);
 THROW 51000, 'Image FK accepted invalid variant', 1;
END TRY
BEGIN CATCH
 IF ERROR_NUMBER()<>547 THROW;
END CATCH;
ROLLBACK;
SELECT 'PASS migration, NO ACTION, filtered index, rowversion, inventory CHECK, SKU UQ, image FK; rolled back' AS Result;
