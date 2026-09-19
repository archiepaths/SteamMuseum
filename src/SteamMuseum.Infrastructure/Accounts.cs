using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SteamMuseum.Application;
using SteamMuseum.Domain;

namespace SteamMuseum.Infrastructure;

public sealed record CreateAccountRequest(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress, System.ComponentModel.DataAnnotations.MaxLength(256)] string Email,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(150)] string DisplayName,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(12), System.ComponentModel.DataAnnotations.MaxLength(256)] string TemporaryPassword,
    [System.ComponentModel.DataAnnotations.Required] string[] Roles);
public sealed record AccountView(Guid Id, string Email, string DisplayName, bool Active, bool MustChangePassword, IList<string> Roles);

public sealed class AccountService(UserManager<MuseumUser> users, SignInManager<MuseumUser> signIn, MuseumDbContext db, IStore store, TimeProvider clock)
{
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
    public async Task<AccountView> View(ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal) ?? throw new BusinessException("Sign in required.", 401);
        var member = await db.Set<Member>().SingleAsync(x => x.Id == user.Id, ct);
        return new(user.Id, user.Email!, member.DisplayName, member.Active, user.MustChangePassword, await users.GetRolesAsync(user));
    }
    public async Task<bool> Login(string email, string password, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || !await db.Set<Member>().AnyAsync(x => x.Id == user.Id && x.Active, ct)) return false;
        var result = await signIn.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded;
    }
    public async Task Logout(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is not null) Check(await users.UpdateSecurityStampAsync(user));
        await signIn.SignOutAsync();
    }
    public Task<AccountView> Create(Guid actor, CreateAccountRequest request, CancellationToken ct) => store.Write(async () => {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 150) throw new BusinessException("Display name is required (maximum 150 characters).");
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email)) throw new BusinessException("A valid email is required.");
        if (request.Roles is null || request.Roles.Any(x => !AccessRoles.All.Contains(x))) throw new BusinessException("Unknown access role.");
        var user = new MuseumUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email };
        Check(await users.CreateAsync(user, request.TemporaryPassword));
        Check(await users.AddToRolesAsync(user, request.Roles.Append(AccessRoles.Member).Distinct()));
        var member = new Member { Id = user.Id, DisplayName = request.DisplayName.Trim() };
        db.Add(member);
        db.Add(new AuditEntry { ActorId = actor, AtUtc = clock.GetUtcNow().UtcDateTime, Action = "AccountCreated", Details = $"Member {user.Id}; roles: {string.Join(',', request.Roles)}" });
        await db.SaveChangesAsync(ct);
        return new AccountView(user.Id, user.Email!, member.DisplayName, true, true, await users.GetRolesAsync(user));
    }, ct);
    public async Task ChangePassword(ClaimsPrincipal principal, string currentPassword, string newPassword)
    {
        var user = await users.GetUserAsync(principal) ?? throw new BusinessException("Sign in required.", 401);
        if (currentPassword == newPassword) throw new BusinessException("Choose a different password.");
        Check(await users.ChangePasswordAsync(user, currentPassword, newPassword));
        user.MustChangePassword = false;
        Check(await users.UpdateAsync(user));
        await signIn.RefreshSignInAsync(user);
    }
    public Task SetActive(Guid actor, Guid id, bool active, CancellationToken ct) => store.Write(async () => {
        if (actor == id) throw new BusinessException("You cannot deactivate your own account.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new BusinessException("Member not found.", 404);
        var member = await db.Set<Member>().SingleAsync(x => x.Id == id, ct);
        member.Active = active;
        Check(await users.UpdateSecurityStampAsync(user));
        db.Add(new AuditEntry { ActorId = actor, AtUtc = clock.GetUtcNow().UtcDateTime, Action = "AccountActiveChanged", Details = $"Member {id}; active={active}" });
        await db.SaveChangesAsync(ct); return true;
    }, ct);
    public Task SetRoles(Guid actor, Guid id, string[] roles, CancellationToken ct) => store.Write(async () => {
        if (roles is null || roles.Any(x => !AccessRoles.All.Contains(x))) throw new BusinessException("Unknown access role.");
        if (actor == id && !roles.Contains(AccessRoles.Administrator)) throw new BusinessException("You cannot remove your own administrator access.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new BusinessException("Member not found.", 404);
        var desired = roles.Append(AccessRoles.Member).Distinct().ToArray();
        var existing = await users.GetRolesAsync(user);
        Check(await users.RemoveFromRolesAsync(user, existing.Except(desired)));
        Check(await users.AddToRolesAsync(user, desired.Except(existing)));
        Check(await users.UpdateSecurityStampAsync(user));
        db.Add(new AuditEntry { ActorId = actor, AtUtc = clock.GetUtcNow().UtcDateTime, Action = "AccountRolesChanged", Details = $"Member {id}; roles: {string.Join(',', desired)}" });
        await db.SaveChangesAsync(ct); return true;
    }, ct);
    public Task ResetPassword(Guid actor, Guid id, string temporaryPassword, CancellationToken ct) => store.Write(async () => {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new BusinessException("Member not found.", 404);
        Check(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), temporaryPassword));
        user.MustChangePassword = true; Check(await users.UpdateAsync(user));
        db.Add(new AuditEntry { ActorId = actor, AtUtc = clock.GetUtcNow().UtcDateTime, Action = "PasswordReset", Details = $"Member {id}" });
        await db.SaveChangesAsync(ct); return true;
    }, ct);
}

