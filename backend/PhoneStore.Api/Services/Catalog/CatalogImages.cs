using System.Buffers.Binary;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.DTOs.Catalog;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.Catalog;

public interface ICatalogImageStore
{
    bool IsConfigured { get; }
    bool UsesCloudinary => false;
    Task SaveAsync(string name, byte[] bytes, CancellationToken ct);
    Task<byte[]?> ReadAsync(string name, CancellationToken ct);
    async Task<CatalogImageContent?> ReadSizedAsync(string name, CatalogImageSize size, CancellationToken ct)
    {
        var bytes = await ReadAsync(name, ct);
        return bytes is null ? null : new(bytes, "image/png");
    }
    Task DeleteAsync(string name);
}
public enum CatalogImageSize { Original, Thumbnail, Card, Display }
public sealed record CatalogImageContent(byte[] Bytes, string ContentType);
public sealed class LocalCatalogImageStore(IHostEnvironment environment, IConfiguration configuration) : ICatalogImageStore
{
    public bool IsConfigured => environment.IsDevelopment();
    private string DirectoryPath => Path.GetFullPath(configuration["Catalog:ImagePath"] ?? Path.Combine(environment.ContentRootPath, ".local-catalog-images"));
    private string FilePath(string name)
    {
        if (name.Length != 36 || !name.EndsWith(".png", StringComparison.Ordinal) || !Guid.TryParseExact(name[..32], "N", out _)) throw new InvalidOperationException("Invalid managed image key.");
        return Path.Combine(DirectoryPath, name);
    }
    public async Task SaveAsync(string name, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(DirectoryPath);
        await using var stream = new FileStream(FilePath(name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, ct);
    }
    public async Task<byte[]?> ReadAsync(string name, CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(FilePath(name), ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public Task DeleteAsync(string name) { File.Delete(FilePath(name)); return Task.CompletedTask; }
}

// Deliberately narrow PNG decoder: only 8-bit RGB/RGBA, no interlace, palette or ancillary data in the stored file.
public static class CatalogPng
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static CatalogException Invalid() => new(400, "INVALID_IMAGE", "Ảnh phải là PNG RGB/RGBA 8-bit hợp lệ, không interlace, tối đa 2 MiB và 2048×2048.");
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); }
        return ~crc;
    }
    public static byte[] Validate(byte[] input)
    {
        if (input.Length > MaxBytes) throw new CatalogException(413, "IMAGE_TOO_LARGE", "Ảnh vượt 2 MiB.");
        if (input.Length < 45 || !input.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw Invalid();
        using var compressed = new MemoryStream();
        var offset = 8; var width = 0; var height = 0; var channels = 0; var seenData = false; var dataEnded = false; var ended = false;
        while (offset + 12 <= input.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset, 4));
            if (length > MaxBytes || (long)offset + 12 + length > input.Length) throw Invalid();
            var size = (int)length; var type = input.AsSpan(offset + 4, 4); var data = input.AsSpan(offset + 8, size);
            if (Crc(input.AsSpan(offset + 4, size + 4)) != BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset + 8 + size, 4))) throw Invalid();
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8 || size != 13) throw Invalid();
                var w = BinaryPrimitives.ReadUInt32BigEndian(data[..4]); var h = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                if (w is 0 or > 2048 || h is 0 or > 2048 || data[8] != 8 || data[9] is not (2 or 6) || data[10] != 0 || data[11] != 0 || data[12] != 0) throw Invalid();
                width = (int)w; height = (int)h; channels = data[9] == 2 ? 3 : 4;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (width == 0 || dataEnded) throw Invalid();
                seenData = true; compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (size != 0 || !seenData || offset + 12 != input.Length) throw Invalid();
                ended = true;
            }
            else
            {
                // Unknown critical chunks and invalid chunk names are not accepted. Drop metadata and executable ancillary payloads.
                if (width == 0 || (type[0] & 32) == 0 || (type[2] & 32) != 0 || type.ToArray().Any(x => x is not (>= 65 and <= 90) and not (>= 97 and <= 122))) throw Invalid();
                if (seenData) dataEnded = true;
            }
            offset += size + 12;
            if (ended) break;
        }
        if (!ended) throw Invalid();
        compressed.Position = 0;
        using var sanitized = new MemoryStream();
        try
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var encoder = new ZLibStream(sanitized, CompressionLevel.Fastest, true);
            var row = new byte[width * channels];
            for (var y = 0; y < height; y++)
            {
                var filter = zlib.ReadByte(); if (filter is < 0 or > 4) throw Invalid();
                zlib.ReadExactly(row);
                encoder.WriteByte((byte)filter); encoder.Write(row);
            }
            if (zlib.ReadByte() != -1) throw Invalid();
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException) { throw Invalid(); }
        // Recompress decoded raster so trailing compressed payloads and ancillary metadata are never stored.
        using var output = new MemoryStream(); output.Write(input, 0, 33);
        var payload = sanitized.ToArray(); var chunk = new byte[payload.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)payload.Length); "IDAT"u8.CopyTo(chunk.AsSpan(4)); payload.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(payload.Length + 8, 4), Crc(chunk.AsSpan(4, payload.Length + 4)));
        output.Write(chunk); output.Write(input, input.Length - 12, 12);
        if (output.Length > MaxBytes) throw new CatalogException(413, "IMAGE_TOO_LARGE", "Ảnh chuẩn hóa vượt 2 MiB.");
        return output.ToArray();
    }
}

public sealed partial class CatalogService
{
    private static ImageDto ImageResult(ProductImage image) => new(ApiContract.Id(image.Id), image.VariantId is null ? null : ApiContract.Id(image.VariantId.Value), image.ImageUrl, image.AltText, image.SortOrder);

    public async Task<ImageDto> ImageUploadResultAsync(long productId, Guid actorId, Guid operationKey, CancellationToken ct)
    {
        var operation = await db.CatalogImageUploads.AsNoTracking().SingleOrDefaultAsync(x => x.ActorUserId == actorId && x.OperationKey == operationKey && x.ProductId == productId, ct)
            ?? throw new CatalogException(404, "IMAGE_UPLOAD_NOT_FOUND", "Chưa tìm thấy yêu cầu tải ảnh.");
        if (operation.ImageId is null) throw new CatalogException(409, "IMAGE_UPLOAD_PENDING", "Yêu cầu chưa hoàn tất. Thử lại cùng ảnh.");
        var image = await db.ProductImages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operation.ImageId && x.ProductId == productId, ct)
            ?? throw new CatalogException(409, "IMAGE_UPLOAD_ALREADY_DELETED", "Ảnh của yêu cầu này đã bị xóa.");
        return ImageResult(image);
    }

    public async Task<ImageDto> UploadAsync(long productId, long? variantId, string altText, int sortOrder, byte[] png, ICatalogImageStore store, ILogger logger, Guid actorId, Guid operationKey, byte[] requestHash, CancellationToken ct)
    {
        if (!store.IsConfigured) throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Chưa cấu hình kho ảnh.");
        await db.Database.OpenConnectionAsync(ct);
        var connection = (Microsoft.Data.SqlClient.SqlConnection)db.Database.GetDbConnection();
        var resource = $"PhoneStore.CatalogImage:{actorId:N}:{operationKey:N}";
        var locked = false;
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=10000; SELECT @result;";
                command.Parameters.AddWithValue("@resource", resource);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0) throw new CatalogException(409, "IMAGE_UPLOAD_BUSY", "Yêu cầu đang được xử lý. Thử lại cùng ảnh.");
                locked = true;
            }
            var operation = await db.CatalogImageUploads.SingleOrDefaultAsync(x => x.ActorUserId == actorId && x.OperationKey == operationKey, ct);
            if (operation is not null)
            {
                if (operation.ProductId != productId || operation.VariantId != variantId || !operation.RequestHash.AsSpan().SequenceEqual(requestHash))
                    throw new CatalogException(409, "IMAGE_UPLOAD_PAYLOAD_MISMATCH", "Mã yêu cầu đã dùng cho ảnh hoặc thông tin khác.");
                if (operation.ImageId is not null) return await ImageUploadResultAsync(productId, actorId, operationKey, ct);
            }
            if (!await db.Products.AnyAsync(x => x.Id == productId, ct)) throw Missing();
            if (variantId is not null && !await db.ProductVariants.AnyAsync(x => x.Id == variantId && x.ProductId == productId, ct)) throw new CatalogException(400, "INVALID_VARIANT", "Phiên bản không thuộc sản phẩm.");
            var newOperation = operation is null;
            if (operation is null)
            {
                operation = new CatalogImageUpload { ActorUserId = actorId, OperationKey = operationKey, RequestHash = requestHash, ProductId = productId, VariantId = variantId, ManagedName = (store.UsesCloudinary ? "cloud-" : "") + Guid.NewGuid().ToString("N") + ".png" };
                db.CatalogImageUploads.Add(operation);
                await db.SaveChangesAsync(ct);
            }
            var name = operation.ManagedName;
            var commitAttempted = false;
            var saved = false;
            try
            {
                // Pending uploads retain a stable public ID even when the provider acknowledgment is lost.
                var existing = newOperation ? null : await store.ReadAsync(name, ct);
                if (existing is not null && !store.UsesCloudinary)
                {
                    try { CatalogPng.Validate(existing); }
                    catch (CatalogException) { await store.DeleteAsync(name); existing = null; }
                }
                if (existing is null) await store.SaveAsync(name, png, ct);
                saved = true;
                // Keep the session lock, but hold no SQL transaction during remote I/O.
                await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
                if (!await db.Products.AnyAsync(x => x.Id == productId, ct)) throw Missing();
                if (variantId is not null && !await db.ProductVariants.AnyAsync(x => x.Id == variantId && x.ProductId == productId, ct)) throw new CatalogException(400, "INVALID_VARIANT", "Phiên bản không thuộc sản phẩm.");
                var image = new ProductImage { ProductId = productId, VariantId = variantId, ImageUrl = "/api/v1/catalog-images/" + name, AltText = altText.Trim(), SortOrder = sortOrder };
                db.ProductImages.Add(image); await db.SaveChangesAsync(ct);
                operation.ImageId = image.Id; await db.SaveChangesAsync(ct);
                commitAttempted = true; await transaction.CommitAsync(ct);
                return ImageResult(image);
            }
            catch (Exception error)
            {
                if (!commitAttempted && (saved || !store.UsesCloudinary)) await CleanupAsync(store, name, logger);
                if (commitAttempted) logger.LogWarning("Catalog image commit outcome requires reconciliation.");
                if (error is IOException or UnauthorizedAccessException) throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Không thể lưu ảnh lúc này.");
                throw;
            }
        }
        finally
        {
            // A canceled acquisition may have taken the lock before its acknowledgment was lost.
            // Never return that connection to the pool without a confirmed release.
            if (!locked) Microsoft.Data.SqlClient.SqlConnection.ClearPool(connection);
            try
            {
                if (locked)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
                    command.Parameters.AddWithValue("@resource", resource);
                    await command.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }
            catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                Microsoft.Data.SqlClient.SqlConnection.ClearPool(connection);
                logger.LogWarning("Catalog upload connection lock release failed; connection removed from pool.");
            }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
    public async Task DeleteImageAsync(long id, ICatalogImageStore store, ILogger logger, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await LockAsync(ct);
        var image = await db.ProductImages.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        db.ProductImages.Remove(image); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (image.ImageUrl.StartsWith("/api/v1/catalog-images/", StringComparison.Ordinal)) await CleanupAsync(store, image.ImageUrl.Split('/')[^1], logger);
    }
    private static async Task CleanupAsync(ICatalogImageStore store, string name, ILogger logger)
    {
        try { await store.DeleteAsync(name); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or CatalogException or OperationCanceledException) { logger.LogWarning("Managed catalog image cleanup requires retry."); }
    }
    public async Task<CatalogImageContent> ImageAsync(string name, bool admin, ICatalogImageStore store, CancellationToken ct, CatalogImageSize size = CatalogImageSize.Original)
    {
        if (!CatalogImageInput.IsManagedName(name)) throw Missing();
        var url = "/api/v1/catalog-images/" + name;
        if (!await db.ProductImages.AnyAsync(x => x.ImageUrl == url && (admin || x.Product.IsActive && x.Product.Brand.IsActive && x.Product.Category.IsActive
            && x.Product.Variants.Any(v => v.IsActive) && (x.VariantId == null || x.Variant!.IsActive)), ct)) throw Missing();
        return await store.ReadSizedAsync(name, size, ct) ?? throw Missing();
    }
}
