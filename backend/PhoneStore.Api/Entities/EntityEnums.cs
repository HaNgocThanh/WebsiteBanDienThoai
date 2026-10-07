namespace PhoneStore.Api.Entities;

// Persisted as varchar names with SQL CHECK constraints; keep names stable.
public enum CustomerTierCode
{
    Bronze,
    Silver,
    Gold,
    Diamond
}

public enum SpendEntryKind
{
    OrderCompleted,
    Refund
}

public enum StockReservationStatus
{
    Active,
    Consumed,
    Released
}

public enum InventoryMovementKind
{
    Receive,
    Adjust,
    Reserve,
    Release,
    Dispatch,
    CancelReturn
}

public enum PromotionStatus
{
    Draft,
    Published,
    Disabled
}

public enum PromotionProductScope
{
    All,
    Selected
}

public enum PromotionAudienceScope
{
    All,
    SelectedTiers
}

public enum DiscountType
{
    Percent,
    Fixed
}

public enum OrderStatus
{
    Placed,
    Reviewing,
    Approved,
    Shipping,
    Delivered,
    Completed,
    Cancelled
}

public enum PaymentMethod
{
    COD,
    BankTransfer
}

public enum OrderActorType
{
    Customer,
    Admin,
    System
}

public enum CancellationRequestStatus
{
    Pending,
    Approved,
    Rejected
}

public enum OrderAccessTokenPurpose
{
    ViewOrder,
    ClaimOrder,
    CancelOrder
}

public enum PaymentStatus
{
    Pending,
    Confirmed,
    Rejected
}

public enum RefundStatus
{
    Pending,
    Completed,
    Failed
}

public enum ReviewStatus
{
    Visible,
    Hidden
}

public enum SurveyStatus
{
    Draft,
    Published,
    Closed
}

public enum SurveyQuestionType
{
    SingleChoice,
    MultipleChoice,
    Text
}

public enum IdempotencyRequestStatus
{
    Issued,
    Completed
}

