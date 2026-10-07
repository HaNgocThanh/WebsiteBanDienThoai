namespace PhoneStore.Api.Entities;

public class StockReservation
{
    public long Id { get; set; }
    public long OrderItemId { get; set; }
    public int Quantity { get; set; }
    public StockReservationStatus Status { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public byte[] Version { get; set; } = [];

    public OrderItem OrderItem { get; set; } = null!;
}

