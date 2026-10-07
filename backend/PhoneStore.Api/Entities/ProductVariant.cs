namespace PhoneStore.Api.Entities;

public class ProductVariant
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int StorageGb { get; set; }
    public int RamGb { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public Product Product { get; set; } = null!;
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public Inventory? Inventory { get; set; }
    public ICollection<InventoryMovement> InventoryMovements { get; set; } = new List<InventoryMovement>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

