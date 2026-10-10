using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace PhoneStore.Api.Services.Catalog;

public static class CatalogImageInput
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static bool IsManagedName(string name)
    {
        var key = name.StartsWith("cloud-", StringComparison.Ordinal) ? name[6..] : name;
        return key.Length == 36 && key.EndsWith(".png", StringComparison.Ordinal)
            && Guid.TryParseExact(key[..32], "N", out var id) && id.ToString("N") + ".png" == key;
    }
    public static void Validate(string filename, string contentType, byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new CatalogException(413, "IMAGE_TOO_LARGE", "Ảnh vượt 10 MiB.");
        var span = bytes.AsSpan(); var extension = Path.GetExtension(filename).ToLowerInvariant();
        var mime = contentType.ToLowerInvariant();
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => mime == "image/jpeg" && span.StartsWith(new byte[] { 255, 216, 255 }),
            ".png" => mime == "image/png" && span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => mime == "image/webp" && span.Length >= 12 && span[..4].SequenceEqual("RIFF"u8) && span.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".gif" => mime == "image/gif" && (span.StartsWith("GIF87a"u8) || span.StartsWith("GIF89a"u8)),
            ".bmp" => mime == "image/bmp" && span.StartsWith("BM"u8),
            ".svg" => mime == "image/svg+xml" && IsSvg(bytes),
            _ => false
        };
        if (!valid) throw new CatalogException(400, "INVALID_IMAGE", "File ảnh hoặc định dạng không hợp lệ.");
    }
    private static bool IsSvg(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes });
            reader.MoveToContent();
            if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException) { return false; }
    }
}

// New assets have a separate managed prefix. Legacy local images remain readable.
public sealed class CloudinaryCatalogImageStore : ICatalogImageStore, IDisposable
{
    private readonly LocalCatalogImageStore local;
    private readonly Cloudinary? cloud;
    private readonly string folder;
    private readonly HttpClient downloads = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    public bool UsesCloudinary => true;
    public bool IsConfigured => cloud is not null;
    public CloudinaryCatalogImageStore(IHostEnvironment environment, IConfiguration configuration)
    {
        local = new(environment, configuration);
        folder = configuration["Cloudinary:Folder"] ?? "phonestore/products";
        var cloudName = configuration["Cloudinary:CloudName"];
        var apiKey = configuration["Cloudinary:ApiKey"];
        var apiSecret = configuration["Cloudinary:ApiSecret"];
        if (Regex.IsMatch(folder, "^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$") && folder.Length <= 120
            && !string.IsNullOrWhiteSpace(cloudName) && Regex.IsMatch(cloudName, "^[a-zA-Z0-9_-]+$")
            && !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiSecret))
            cloud = new Cloudinary(new Account(cloudName, apiKey, apiSecret)) { Api = { Secure = true } };
    }
    public string PublicId(string name)
    {
        if (!name.StartsWith("cloud-", StringComparison.Ordinal) || !CatalogImageInput.IsManagedName(name)) throw new InvalidOperationException("Invalid managed cloud image key.");
        return folder + "/" + name[6..^4];
    }
    private Cloudinary Client => cloud ?? throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Chưa cấu hình Cloudinary.");
    public async Task SaveAsync(string name, byte[] bytes, CancellationToken ct)
    {
        var publicId = PublicId(name);
        using var stream = new MemoryStream(bytes);
        try
        {
            var result = await Client.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription("image", stream), PublicId = publicId, AssetFolder = folder,
                Type = "authenticated", Format = "png", Overwrite = false, UniqueFilename = false,
                Transformation = new Transformation().Width(2048).Height(2048).Crop("limit"),
                Tags = "phonestore,product"
            }, ct);
            if (result.Error is not null)
            {
                if (result.StatusCode == HttpStatusCode.BadRequest) throw new CatalogException(400, "INVALID_IMAGE", "Cloudinary không đọc được ảnh này.");
                throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Không thể lưu ảnh trên Cloudinary lúc này.");
            }
            if (result.PublicId != publicId || result.Format != "png" || result.Width <= 0 || result.Height <= 0)
                throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Phản hồi kho ảnh không hợp lệ.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Không thể kết nối kho ảnh lúc này."); }
    }
    public async Task<byte[]?> ReadAsync(string name, CancellationToken ct) => (await ReadSizedAsync(name, CatalogImageSize.Original, ct))?.Bytes;
    public async Task<CatalogImageContent?> ReadSizedAsync(string name, CatalogImageSize size, CancellationToken ct)
    {
        if (!name.StartsWith("cloud-", StringComparison.Ordinal)) { var localBytes = await local.ReadAsync(name, ct); return localBytes is null ? null : new(localBytes, "image/png"); }
        var optimized = size != CatalogImageSize.Original;
        var contentType = optimized ? "image/webp" : "image/png";
        var delivery = Client.Api.UrlImgUp.Secure(true).Type("authenticated").Signed(true).Format(optimized ? "webp" : "png");
        if (optimized) { var width = size switch { CatalogImageSize.Thumbnail => 160, CatalogImageSize.Card => 480, _ => 1280 }; delivery = delivery.Transform(new Transformation().Width(width).Height(width).Crop("limit").Quality("auto")); }
        var url = delivery.BuildUrl(PublicId(name));
        try
        {
            using var response = await downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != contentType)
                throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Không thể tải ảnh lúc này.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream();
            var buffer = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (output.Length + count > 20 * 1024 * 1024) throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Ảnh trả về vượt giới hạn.");
                output.Write(buffer, 0, count);
            }
            var bytes = output.ToArray();
            if (optimized ? bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) : !bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Phản hồi ảnh không hợp lệ.");
            return new(bytes, contentType);
        }
        catch (Exception error) when (error is HttpRequestException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new CatalogException(503, "IMAGE_UNAVAILABLE", "Không thể kết nối kho ảnh lúc này."); }
    }
    public async Task DeleteAsync(string name)
    {
        if (!name.StartsWith("cloud-", StringComparison.Ordinal)) { await local.DeleteAsync(name); return; }
        var result = await Client.DestroyAsync(new DeletionParams(PublicId(name)) { Type = "authenticated", Invalidate = true });
        if (result.Error is not null || result.Result is not ("ok" or "not found")) throw new InvalidOperationException("Cloud image cleanup requires retry.");
    }
    public void Dispose() => downloads.Dispose();
}
