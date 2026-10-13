using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Reviews;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;
using PhoneStore.Api.Services.Checkout;
using PhoneStore.Api.Services.Membership;
namespace PhoneStore.Api.Services.Reviews;
public sealed class ReviewService(AppDbContext db,TimeProvider clock,AuthBootstrap users) {
 private DateTime Now=>new(clock.GetUtcNow().UtcTicks/TimeSpan.TicksPerMillisecond*TimeSpan.TicksPerMillisecond,DateTimeKind.Utc);
 private static CheckoutException Error(int status,string code)=>new(status,code,"Không thể thực hiện đánh giá này. Kiểm tra quyền sở hữu, trạng thái đơn và tải lại dữ liệu.");
 private static string Text(string? value,int max){var text=value?.Trim().Normalize(NormalizationForm.FormC)??"";if(text.Length is 0||text.Length>max)throw Error(400,"VALIDATION_ERROR");return text;}
 private static bool Received(OrderStatus status)=>status is OrderStatus.Delivered or OrderStatus.Completed;
 private static void Version(string? header,byte[] actual){if(header is null)throw Error(428,"PRECONDITION_REQUIRED");if(!ApiContract.TryParseETag(header,out var version))throw Error(400,"INVALID_ETAG");if(!CryptographicOperations.FixedTimeEquals(version,actual))throw Error(412,"VERSION_CONFLICT");}
 private async Task Actor(Guid user,CancellationToken ct){await MembershipService.LockAsync(db,false,ct);await users.LockUserAsync(user,ct);if(!await db.Users.AnyAsync(u=>u.Id==user&&u.IsActive&&u.EmailConfirmed,ct))throw Error(401,"UNAUTHENTICATED");}
 private async Task<Order> LockOrder(long id,CancellationToken ct)=>await db.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct)??throw Error(404,"NOT_FOUND");
 private static OwnerReviewDto Owner(Review r)=>new(r.Id.ToString(),r.OrderItemId.ToString(),r.Rating,r.Content,r.Status.ToString(),r.ModerationReason,r.AdminReply,r.CreatedAt,r.UpdatedAt,Convert.ToBase64String(r.Version));
 private void Audit(Review r,Guid actor,string action,string text)=>db.AuditLogs.Add(new(){ActorUserId=actor,Action="Review."+action,EntityType="Review",EntityId=r.Id.ToString(),Summary="sha256:"+Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),CorrelationId=Guid.NewGuid().ToString(),CreatedAt=Now});
 public async Task<IReadOnlyList<ItemReviewDto>> Mine(long orderId,Guid user,CancellationToken ct){
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Actor(user,ct);var order=await db.Orders.AsNoTracking().SingleOrDefaultAsync(o=>o.Id==orderId&&o.UserId==user,ct)??throw Error(404,"NOT_FOUND");
  var rows=await db.OrderItems.AsNoTracking().Where(i=>i.OrderId==orderId).OrderBy(i=>i.Id).Select(i=>new{i.Id,i.ProductNameSnapshot,i.VariantSnapshot,i.Review}).ToListAsync(ct);
  var result=rows.Select(i=>new ItemReviewDto(i.Id.ToString(),i.ProductNameSnapshot,i.VariantSnapshot,Received(order.Status),i.Review is null?null:Owner(i.Review))).ToArray();await tx.CommitAsync(ct);return result;
 }
 public async Task<OwnerReviewDto> Create(long itemId,Guid user,ReviewInput input,CancellationToken ct){
  var text=Text(input.Content,2000);if(input.Rating is <1 or >5)throw Error(400,"VALIDATION_ERROR");await using var tx=await db.Database.BeginTransactionAsync(ct);await Actor(user,ct);
  var item=await db.OrderItems.AsNoTracking().Where(i=>i.Id==itemId&&i.Order.UserId==user).Select(i=>new{i.OrderId}).SingleOrDefaultAsync(ct)??throw Error(404,"NOT_FOUND");var order=await LockOrder(item.OrderId,ct);if(order.UserId!=user)throw Error(404,"NOT_FOUND");if(!Received(order.Status))throw Error(409,"REVIEW_NOT_RECEIVED");
  if(await db.Reviews.AnyAsync(r=>r.OrderItemId==itemId,ct))throw Error(409,"REVIEW_EXISTS");
  var r=new Review{OrderItemId=itemId,UserId=user,Rating=(byte)input.Rating,Content=text,Status=ReviewStatus.Visible,CreatedAt=Now};db.Reviews.Add(r);await db.SaveChangesAsync(ct);Audit(r,user,"Create",text);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Owner(r);
 }
 public async Task<OwnerReviewDto> Edit(long id,Guid user,ReviewInput input,string? version,CancellationToken ct){
  var text=Text(input.Content,2000);if(input.Rating is <1 or >5)throw Error(400,"VALIDATION_ERROR");await using var tx=await db.Database.BeginTransactionAsync(ct);await Actor(user,ct);
  var orderId=await db.Reviews.AsNoTracking().Where(r=>r.Id==id&&r.UserId==user).Select(r=>(long?)r.OrderItem.OrderId).SingleOrDefaultAsync(ct)??throw Error(404,"NOT_FOUND");var order=await LockOrder(orderId,ct);if(order.UserId!=user)throw Error(404,"NOT_FOUND");if(!Received(order.Status))throw Error(409,"REVIEW_NOT_RECEIVED");
  var r=await db.Reviews.FromSqlInterpolated($"SELECT * FROM [Reviews] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);Version(version,r.Version);r.Content=text;r.Rating=(byte)input.Rating;r.UpdatedAt=Now;Audit(r,user,"Edit",text);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Owner(r);
 }
 public async Task<OwnerReviewDto> Moderate(long id,Guid actor,string? version,ReviewModeration? input,ReviewReply? reply,CancellationToken ct){
  var text=Text(input?.Reason??reply?.Content,input is null?2000:500);if(input is not null&&input.Status is not("Hidden" or "Visible"))throw Error(400,"VALIDATION_ERROR");
  await using var tx=await db.Database.BeginTransactionAsync(ct);await Actor(actor,ct);var orderId=await db.Reviews.AsNoTracking().Where(r=>r.Id==id).Select(r=>(long?)r.OrderItem.OrderId).SingleOrDefaultAsync(ct)??throw Error(404,"NOT_FOUND");await LockOrder(orderId,ct);
  var r=await db.Reviews.FromSqlInterpolated($"SELECT * FROM [Reviews] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);Version(version,r.Version);
  if(input is not null){r.Status=Enum.Parse<ReviewStatus>(input.Status);r.ModerationReason=text;r.ModeratedAt=Now;r.ModeratedByUserId=actor;}else{r.AdminReply=text;r.RepliedAt=Now;r.RepliedByUserId=actor;}
  Audit(r,actor,input is null?"Reply":"Moderate",text);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Owner(r);
 }
 private static void Page(PageQuery p){if(p.Offset>int.MaxValue)throw Error(400,"INVALID_FILTER");}
 public async Task<PagedResponse<AdminReviewDto>> Admin(PageQuery page,string? status,CancellationToken ct){Page(page);var q=db.Reviews.AsNoTracking();if(status is not null){if(!Enum.TryParse<ReviewStatus>(status,out var value)||value.ToString()!=status)throw Error(400,"INVALID_FILTER");q=q.Where(r=>r.Status==value);}
  var total=await q.LongCountAsync(ct);var rows=await q.OrderByDescending(r=>r.CreatedAt).ThenByDescending(r=>r.Id).Skip((int)page.Offset).Take(page.PageSize).Select(r=>new{Review=r,r.OrderItem.Variant.ProductId,r.OrderItem.ProductNameSnapshot,r.OrderItem.VariantSnapshot,r.OrderItem.Order.OrderNumber}).ToListAsync(ct);
  return new(rows.Select(x=>new AdminReviewDto(Owner(x.Review),x.ProductId.ToString(),x.ProductNameSnapshot,x.VariantSnapshot,x.OrderNumber,x.Review.UserId)).ToArray(),page.Page,page.PageSize,total);
 }
 public async Task<PublicReviewPage> Public(long product,PageQuery page,CancellationToken ct){Page(page);
  // Serializable read transaction keeps visibility, count, average and page from one DB snapshot.
  await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
  if(!await db.Products.AnyAsync(p=>p.Id==product&&p.IsActive&&p.Brand.IsActive&&p.Category.IsActive,ct))throw Error(404,"NOT_FOUND");
  var q=db.Reviews.AsNoTracking().Where(r=>r.OrderItem.Variant.ProductId==product&&r.Status==ReviewStatus.Visible);var count=await q.LongCountAsync(ct);var average=await q.Select(r=>(decimal?)r.Rating).AverageAsync(ct);
  var items=await q.OrderByDescending(r=>r.CreatedAt).ThenByDescending(r=>r.Id).Skip((int)page.Offset).Take(page.PageSize).Select(r=>new PublicReviewDto(r.Id.ToString(),r.Rating,r.Content,r.OrderItem.VariantSnapshot,r.AdminReply,r.CreatedAt,r.UpdatedAt)).ToListAsync(ct);
  await tx.CommitAsync(ct);return new(items,page.Page,page.PageSize,count,average is null?null:decimal.Round(average.Value,2));
 }
}
