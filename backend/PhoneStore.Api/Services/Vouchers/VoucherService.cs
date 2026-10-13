using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Pricing;
using PhoneStore.Api.Services.Checkout;
namespace PhoneStore.Api.Services.Vouchers;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoucherInput([Required,MaxLength(40)] string Code,[Required,MaxLength(150)] string Name,string DiscountType,decimal DiscountValue,decimal? MaxDiscount,decimal MinimumSpend,DateTime StartsAt,DateTime EndsAt,int TotalLimit,int? PerUserLimit,string[] ProductIds,string[] TierIds,Guid OperationKey);
public sealed record VoucherDto(string Id,string Code,string Name,string Status,string DiscountType,decimal DiscountValue,decimal? MaxDiscount,decimal MinimumSpend,DateTime StartsAt,DateTime EndsAt,int TotalLimit,int? PerUserLimit,string[] ProductIds,string[] TierIds,string Version,int Used);
public sealed class VoucherService(AppDbContext db,TimeProvider clock) {
 public DateTime Now => clock.GetUtcNow().UtcDateTime;
 public static string? Normalize(string? code) => string.IsNullOrWhiteSpace(code)?null:code.Trim().ToUpperInvariant();
 private static CheckoutException Error(string code,int status=409) => new(status,code,"Voucher không hợp lệ, đã hết hạn, không đủ điều kiện hoặc hết lượt. Hãy kiểm tra lại.");
 public static async Task LockAsync(AppDbContext db,CancellationToken ct) => await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PhoneStore.Vouchers',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000; IF @r<0 THROW 51004,'Voucher busy.',1;",ct);
 private static string[] Ids(string json)=>JsonSerializer.Deserialize<string[]>(json)!;
 private async Task<VoucherDto> Dto(Voucher v,CancellationToken ct)=>new(v.Id.ToString(),v.Code,v.Name,v.Status,v.DiscountType,v.DiscountValue,v.MaxDiscount,v.MinimumSpend,v.StartsAt,v.EndsAt,v.TotalLimit,v.PerUserLimit,Ids(v.ProductIds),Ids(v.TierIds),Convert.ToBase64String(v.Version),await db.VoucherUses.CountAsync(x=>x.VoucherId==v.Id&&!x.Released,ct));
 public async Task<PagedResponse<VoucherDto>> List(PageQuery page,CancellationToken ct) {
  var rows=await db.Vouchers.AsNoTracking().OrderByDescending(v=>v.Id).Skip((int)Math.Min(page.Offset,int.MaxValue)).Take(page.PageSize).ToListAsync(ct);
  var data=new List<VoucherDto>();foreach(var v in rows)data.Add(await Dto(v,ct));return new(data,page.Page,page.PageSize,await db.Vouchers.LongCountAsync(ct));
 }
 public async Task<VoucherDto> Create(VoucherInput i,Guid actor,CancellationToken ct) {
  var code=Normalize(i.Code);var products=i.ProductIds?.Distinct().Order().ToArray();var tiers=i.TierIds?.Distinct().Order().ToArray();
  if(code is null||!Regex.IsMatch(code,"^[A-Z0-9][A-Z0-9_-]{2,39}$")||string.IsNullOrWhiteSpace(i.Name)||i.OperationKey==Guid.Empty||i.DiscountType is not("Percent" or "Fixed")||i.DiscountValue<=0||i.DiscountValue>ApiContract.MaxSafeMoney||i.DiscountType=="Percent"&&(i.DiscountValue>100||decimal.Round(i.DiscountValue,2)!=i.DiscountValue)||i.DiscountType=="Fixed"&&decimal.Truncate(i.DiscountValue)!=i.DiscountValue||i.MaxDiscount is {} cap&&(cap<=0||cap>ApiContract.MaxSafeMoney||decimal.Truncate(cap)!=cap)||i.MinimumSpend<0||i.MinimumSpend>ApiContract.MaxSafeMoney||decimal.Truncate(i.MinimumSpend)!=i.MinimumSpend||i.StartsAt.Kind!=DateTimeKind.Utc||i.EndsAt.Kind!=DateTimeKind.Utc||i.EndsAt<=i.StartsAt||i.TotalLimit<1||i.PerUserLimit is {} per&&(per<1||per>i.TotalLimit)||products is null||tiers is null||products.Length>500||tiers.Length>4||products.Concat(tiers).Any(x=>!ApiContract.TryParseId(x,out _)))throw Error("VALIDATION_ERROR",400);
  var hash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(i with {Code=code,Name=i.Name.Trim(),ProductIds=products,TierIds=tiers}));
  await using var tx=await db.Database.BeginTransactionAsync(ct);await LockAsync(db,ct);
  var existing=await db.Vouchers.SingleOrDefaultAsync(v=>v.CreationKey==i.OperationKey,ct);
  if(existing is not null){if(!CryptographicOperations.FixedTimeEquals(existing.RequestHash,hash))throw Error("IDEMPOTENCY_PAYLOAD_MISMATCH");var r=await Dto(existing,ct);await tx.CommitAsync(ct);return r;}
  if(await db.Vouchers.AnyAsync(v=>v.Code==code,ct))throw Error("VOUCHER_CODE_EXISTS");
  var pids=products.Select(long.Parse).ToArray();var tids=tiers.Select(long.Parse).ToArray();
  if(await db.Products.CountAsync(p=>pids.Contains(p.Id),ct)!=pids.Length||await db.CustomerTiers.CountAsync(t=>tids.Contains(t.Id),ct)!=tids.Length)throw Error("VALIDATION_ERROR",400);
  var v=new Voucher{Code=code,Name=i.Name.Trim(),DiscountType=i.DiscountType,DiscountValue=i.DiscountValue,MaxDiscount=i.MaxDiscount,MinimumSpend=i.MinimumSpend,StartsAt=i.StartsAt,EndsAt=i.EndsAt,TotalLimit=i.TotalLimit,PerUserLimit=i.PerUserLimit,ProductIds=JsonSerializer.Serialize(products),TierIds=JsonSerializer.Serialize(tiers),CreationKey=i.OperationKey,RequestHash=hash};db.Vouchers.Add(v);await db.SaveChangesAsync(ct);Audit(v,actor,"Create");await db.SaveChangesAsync(ct);var result=await Dto(v,ct);await tx.CommitAsync(ct);return result;
 }
 private void Audit(Voucher v,Guid actor,string action)=>db.AuditLogs.Add(new(){ActorUserId=actor,Action="Voucher."+action,EntityType="Voucher",EntityId=v.Id.ToString(),Summary=action,CorrelationId=Guid.NewGuid().ToString(),CreatedAt=Now});
 public async Task<VoucherDto> Action(long id,string target,string? version,Guid actor,CancellationToken ct){
  await using var tx=await db.Database.BeginTransactionAsync(ct);await LockAsync(db,ct);var v=await db.Vouchers.SingleOrDefaultAsync(v=>v.Id==id,ct)??throw Error("NOT_FOUND",404);
  if(v.Status==target){var replay=await Dto(v,ct);await tx.CommitAsync(ct);return replay;}
  if(version is null)throw Error("PRECONDITION_REQUIRED",428);if(version.Trim('"')!=Convert.ToBase64String(v.Version))throw Error("PRECONDITION_FAILED",412);
  if(target=="Published"&&(v.Status!="Draft"||v.EndsAt<=Now)||target=="Disabled"&&v.Status!="Published")throw Error("VOUCHER_STATE");
  v.Status=target;Audit(v,actor,target);await db.SaveChangesAsync(ct);var r=await Dto(v,ct);await tx.CommitAsync(ct);return r;
 }
 public async Task<QuoteDto> Apply(QuoteDto quote,string? code,Guid? user,CancellationToken ct){
  code=Normalize(code);if(code is null)return quote;await LockAsync(db,ct);
  var v=await db.Vouchers.AsNoTracking().SingleOrDefaultAsync(v=>v.Code==code,ct);var now=Now;
  if(v is null||v.Status!="Published"||v.StartsAt>now||v.EndsAt<=now)throw Error("VOUCHER_UNAVAILABLE");
  var tiers=Ids(v.TierIds);if((v.PerUserLimit is not null||tiers.Length>0)&&user is null)throw Error("VOUCHER_LOGIN_REQUIRED",400);
  if(tiers.Length>0){var tier=await db.CustomerProfiles.Where(p=>p.UserId==user).Select(p=>p.TierId).SingleOrDefaultAsync(ct);if(tier is null||!tiers.Contains(tier.ToString()!))throw Error("VOUCHER_INELIGIBLE");}
  var productIds=Ids(v.ProductIds);var scopedProducts=productIds.Select(long.Parse).ToArray();var variantIds=quote.Items.Select(i=>long.Parse(i.VariantId)).ToArray();var applicable=productIds.Length==0?variantIds:await db.ProductVariants.Where(x=>variantIds.Contains(x.Id)&&scopedProducts.Contains(x.ProductId)).Select(x=>x.Id).ToArrayAsync(ct);
  var basis=quote.Items.Where(i=>applicable.Contains(long.Parse(i.VariantId))).Sum(i=>i.LineTotal);
  if(basis<=0||basis<v.MinimumSpend)throw Error("VOUCHER_MINIMUM_SPEND");
  if(await db.VoucherUses.CountAsync(u=>u.VoucherId==v.Id&&!u.Released,ct)>=v.TotalLimit||v.PerUserLimit is {} max&&await db.VoucherUses.CountAsync(u=>u.VoucherId==v.Id&&!u.Released&&u.UserId==user,ct)>=max)throw Error("VOUCHER_LIMIT");
  var discount=Discount(v,basis);if(discount<=0)throw Error("VOUCHER_INELIGIBLE");
  var rule=JsonSerializer.Serialize(new{v.Id,v.Code,v.Name,v.DiscountType,v.DiscountValue,v.MaxDiscount,v.MinimumSpend,v.StartsAt,v.EndsAt,v.ProductIds,v.TierIds,v.TotalLimit,v.PerUserLimit});
  var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{quote.QuoteHash,rule,discount})));
  return quote with{DiscountTotal=quote.DiscountTotal+discount,GrandTotal=quote.GrandTotal-discount,QuoteHash=hash,VoucherCode=code,VoucherDiscountTotal=discount,VoucherId=v.Id,VoucherRule=rule};
 }
 public static decimal Discount(Voucher v,decimal basis)=>Math.Min(basis,Math.Min(v.MaxDiscount??decimal.MaxValue,v.DiscountType=="Percent"?decimal.Floor(basis*v.DiscountValue/100):v.DiscountValue));
 public void Consume(QuoteDto quote,Order order){if(quote.VoucherId is {} id){using var rule=JsonDocument.Parse(quote.VoucherRule!);if(rule.RootElement.GetProperty("EndsAt").GetDateTime()<=Now)throw Error("PRICE_CHANGED");db.VoucherUses.Add(new(){VoucherId=id,OrderId=order.Id,UserId=order.UserId});}}
 public static async Task Release(AppDbContext db,long orderId,CancellationToken ct){await LockAsync(db,ct);var use=await db.VoucherUses.SingleOrDefaultAsync(v=>v.OrderId==orderId,ct);if(use is not null)use.Released=true;}
}
