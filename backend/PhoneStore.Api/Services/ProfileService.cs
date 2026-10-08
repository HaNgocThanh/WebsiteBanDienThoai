using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.DTOs.Common;
using PhoneStore.Api.DTOs.Profile;
using PhoneStore.Api.Entities;
using PhoneStore.Api.Services.Auth;

namespace PhoneStore.Api.Services;

public sealed class ProfileService(AppDbContext db, UserManager<ApplicationUser> users, AuthBootstrap locks, TimeProvider clock)
{
    public async Task<ProfileDto> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, cancellationToken);
        var membership = await db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { TierCode = x.Tier.Code.ToString(), x.EligibleSpend }).SingleOrDefaultAsync(cancellationToken);
        return new(user.Id, user.Email!, user.FullName, user.PhoneNumber, membership?.TierCode, ApiContract.Money(membership?.EligibleSpend ?? 0));
    }
    public async Task<IdentityResult> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName)) return IdentityResult.Failed(new IdentityError { Code = "InvalidName" });
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await locks.LockUserAsync(userId, cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("Authenticated account missing.");
        await db.Entry(user).ReloadAsync(cancellationToken);
        user.FullName = request.FullName.Trim(); user.PhoneNumber = request.Phone?.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return result;
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
    public Task<List<AddressDto>> AddressesAsync(Guid userId, CancellationToken cancellationToken) => db.Addresses.AsNoTracking()
        .Where(x => x.UserId == userId).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id)
        .Select(x => new AddressDto(x.Id.ToString(), x.RecipientName, x.Phone, x.AddressLine, x.Locality, x.Province, x.CountryCode, x.IsDefault)).ToListAsync(cancellationToken);
    public Task<AddressDto?> AddressAsync(Guid userId, long id, CancellationToken cancellationToken) => db.Addresses.AsNoTracking()
        .Where(x => x.UserId == userId && x.Id == id)
        .Select(x => new AddressDto(x.Id.ToString(), x.RecipientName, x.Phone, x.AddressLine, x.Locality, x.Province, x.CountryCode, x.IsDefault)).SingleOrDefaultAsync(cancellationToken);

    public async Task<AddressDto?> WriteAddressAsync(Guid userId, long? id, AddressRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await locks.LockUserAsync(userId, cancellationToken);
        var address = id is null ? new Address { UserId = userId, CreatedAt = clock.GetUtcNow().UtcDateTime }
            : await db.Addresses.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (address is null) return null;
        if (request.IsDefault)
            await db.Addresses.Where(x => x.UserId == userId && x.IsDefault && x.Id != (id ?? 0))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false).SetProperty(x => x.UpdatedAt, clock.GetUtcNow().UtcDateTime), cancellationToken);
        address.RecipientName = request.RecipientName.Trim(); address.Phone = request.Phone.Trim();
        address.AddressLine = request.AddressLine.Trim(); address.Locality = request.Locality?.Trim();
        address.Province = request.Province.Trim(); address.CountryCode = request.CountryCode.ToUpperInvariant(); address.IsDefault = request.IsDefault;
        if (id is not null) address.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        if (id is null) db.Addresses.Add(address);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ApiContract.Id(address.Id), address.RecipientName, address.Phone, address.AddressLine, address.Locality, address.Province, address.CountryCode, address.IsDefault);
    }
    public async Task<bool> DeleteAddressAsync(Guid userId, long id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await locks.LockUserAsync(userId, cancellationToken);
        var deleted = await db.Addresses.Where(x => x.UserId == userId && x.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted == 1;
    }
}
