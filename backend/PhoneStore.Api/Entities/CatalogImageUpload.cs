namespace PhoneStore.Api.Entities;

public sealed class CatalogImageUpload
{
    public Guid ActorUserId { get; set; }
    public Guid OperationKey { get; set; }
    public byte[] RequestHash { get; set; } = [];
    public long ProductId { get; set; }
    public long? VariantId { get; set; }
    public string ManagedName { get; set; } = "";
    // Retained after image deletion so retry cannot recreate a deleted upload.
    public long? ImageId { get; set; }
    public DateTime CreatedAt { get; set; }
}
