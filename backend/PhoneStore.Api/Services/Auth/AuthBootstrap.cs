using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Data;
using PhoneStore.Api.Entities;

namespace PhoneStore.Api.Services.Auth;

public sealed class AuthBootstrap(AppDbContext db, RoleManager<IdentityRole<Guid>> roles, UserManager<ApplicationUser> users)
{
    // Caller owns the DB transaction. Serialize bootstrap/registration across API processes,
    // preserving unique email/role/tier invariants without swallowing unrelated SQL conflicts.
    public async Task LockAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'PhoneStore.Auth.Bootstrap',
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51001, 'Auth bootstrap lock unavailable.', 1;
            """, cancellationToken);
    }

    public async Task LockUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var resource = "PhoneStore.Auth.User." + userId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource},
                @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 51001, 'Auth user lock unavailable.', 1;
            """, cancellationToken);
    }

    public async Task<CustomerTier> EnsureAsync(CancellationToken cancellationToken)
    {
        foreach (var name in new[] { "Customer", "Admin" })
        {
            if (!await roles.RoleExistsAsync(name)) Require(await roles.CreateAsync(new IdentityRole<Guid>(name) { Id = Guid.NewGuid() }));
        }
        var bronze = await db.CustomerTiers.SingleOrDefaultAsync(x => x.Code == CustomerTierCode.Bronze, cancellationToken);
        if (bronze is null)
        {
            // D-35: new accounts below one million VND have no tier.
            bronze = new CustomerTier { Code = CustomerTierCode.Bronze, Name = "Bronze", MinimumSpend = 1_000_000, Rank = 1 };
            db.CustomerTiers.Add(bronze);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (bronze.Rank != 1)
            throw new InvalidOperationException("Existing Bronze tier must have Rank=1.");
        return bronze;
    }

    public async Task RunAsync(string? existingAdminEmail, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PhoneStore.Api.Services.Membership.MembershipService.LockAsync(db, false, cancellationToken);
        await LockAsync(cancellationToken);
        await EnsureAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(existingAdminEmail))
        {
            var user = await users.FindByEmailAsync(existingAdminEmail.Trim());
            if (user is null)
                throw new InvalidOperationException("Admin bootstrap requires an existing active, verified account.");
            await LockUserAsync(user.Id, cancellationToken);
            await db.Entry(user).ReloadAsync(cancellationToken);
            if (!user.IsActive || !user.EmailConfirmed)
                throw new InvalidOperationException("Admin bootstrap requires an existing active, verified account.");
            if (!await users.IsInRoleAsync(user, "Admin")) Require(await users.AddToRoleAsync(user, "Admin"));
            Require(await users.UpdateSecurityStampAsync(user));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    internal static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Identity persistence failed.");
    }
}
