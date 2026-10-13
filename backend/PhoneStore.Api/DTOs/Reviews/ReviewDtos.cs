using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PhoneStore.Api.DTOs.Common;
namespace PhoneStore.Api.DTOs.Reviews;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewInput([Range(1,5)] int Rating,[Required,MaxLength(2000)] string Content);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewModeration([Required,RegularExpression("^(Visible|Hidden)$")] string Status,[Required,MaxLength(500)] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewReply([Required,MaxLength(2000)] string Content);
public sealed record OwnerReviewDto(string Id,string OrderItemId,int Rating,string Content,string Status,string? ModerationReason,string? AdminReply,DateTime CreatedAt,DateTime? UpdatedAt,string Version);
public sealed record ItemReviewDto(string OrderItemId,string ProductName,string Variant,bool CanReview,OwnerReviewDto? Review);
public sealed record PublicReviewDto(string Id,int Rating,string Content,string Variant,string? AdminReply,DateTime CreatedAt,DateTime? UpdatedAt);
public sealed record PublicReviewPage(IReadOnlyList<PublicReviewDto> Items,int Page,int PageSize,long TotalCount,decimal? AverageRating);
public sealed record AdminReviewDto(OwnerReviewDto Review,string ProductId,string ProductName,string Variant,string OrderNumber,Guid AuthorUserId);
