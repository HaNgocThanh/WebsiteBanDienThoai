namespace PhoneStore.Api.Entities;
public class Voucher {
 public long Id { get; set; }
 public string Code { get; set; } = "";
 public string Name { get; set; } = "";
 public string Status { get; set; } = "Draft";
 public string DiscountType { get; set; } = "Fixed";
 public decimal DiscountValue { get; set; }
 public decimal? MaxDiscount { get; set; }
 public decimal MinimumSpend { get; set; }
 public DateTime StartsAt { get; set; }
 public DateTime EndsAt { get; set; }
 public int TotalLimit { get; set; }
 public int? PerUserLimit { get; set; }
 public string ProductIds { get; set; } = "[]";
 public string TierIds { get; set; } = "[]";
 public Guid CreationKey { get; set; }
 public byte[] RequestHash { get; set; } = [];
 public byte[] Version { get; set; } = [];
}
public class VoucherUse {
 public long OrderId { get; set; }
 public long VoucherId { get; set; }
 public Guid? UserId { get; set; }
 public bool Released { get; set; }
}
