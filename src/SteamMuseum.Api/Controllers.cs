using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SteamMuseum.Application;
using SteamMuseum.Infrastructure;

namespace SteamMuseum.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AccountService accounts) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        await accounts.Login(request.Email, request.Password, ct) ? NoContent() : Unauthorized();
    [HttpGet("me")]
    public Task<AccountView> Me(CancellationToken ct) => accounts.View(User, ct);
    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await accounts.Logout(User); return NoContent(); }
    [HttpPost("password")]
    public async Task<IActionResult> Password(ChangePasswordRequest request)
    { await accounts.ChangePassword(User, request.CurrentPassword, request.NewPassword); return NoContent(); }
}
public sealed record LoginRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required, MaxLength(256)] string Password);
public sealed record ChangePasswordRequest([Required, MaxLength(256)] string CurrentPassword, [Required, MinLength(12), MaxLength(256)] string NewPassword);
public sealed record NameRequest([Required, MaxLength(150)] string Name);
public sealed record WindowStateRequest(bool Open, DateTime DeadlineUtc);
public sealed record AssignRequest(Guid MemberId);
public sealed record ReasonRequest([Required, MaxLength(500)] string Reason);
public sealed record TrainingRequest(Guid MemberId, DateOnly Date, [Required, MaxLength(2000)] string Notes);
public sealed record ActiveRequest(bool Active);
public sealed record RolesRequest([Required] string[] Roles);
public sealed record ResetPasswordRequest([Required, MinLength(12), MaxLength(256)] string TemporaryPassword);

[ApiController]
[Route("api")]
public sealed class MuseumController(MuseumService museum) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("windows")]
    public async Task<IActionResult> Windows(CancellationToken ct) => Ok(await museum.Windows(ct));
    [HttpPost("windows"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> CreateWindow(WindowRequest request, CancellationToken ct) => Ok(await museum.CreateWindow(Actor, request, ct));
    [HttpPut("windows/{id:guid}/state"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> SetWindow(Guid id, WindowStateRequest request, CancellationToken ct) => Ok(await museum.SetWindowOpen(Actor, id, request.Open, request.DeadlineUtc, ct));
    [HttpGet("me/availability/{windowId:guid}")]
    public async Task<IActionResult> Availability(Guid windowId, CancellationToken ct) => Ok(await museum.Availability(Actor, windowId, ct));
    [HttpPut("me/availability/{windowId:guid}")]
    public async Task<IActionResult> SaveAvailability(Guid windowId, AvailabilityRequest request, CancellationToken ct) => Ok(await museum.SaveAvailability(Actor, windowId, request, ct));
    [HttpGet("members/{memberId:guid}/availability/{windowId:guid}"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> MemberAvailability(Guid memberId, Guid windowId, CancellationToken ct) => Ok(await museum.Availability(memberId, windowId, ct));
    [HttpGet("members"), Authorize(Policy = "StaffRecords")]
    public async Task<IActionResult> Members(CancellationToken ct) => Ok(await museum.Members(ct));
    [HttpGet("railways")]
    public async Task<IActionResult> Railways(CancellationToken ct) => Ok(await museum.Railways(ct));
    [HttpPost("railways"), Authorize(Policy = "Administration")]
    public async Task<IActionResult> CreateRailway(NameRequest request, CancellationToken ct) => Ok(await museum.CreateRailway(Actor, request.Name, ct));
    [HttpGet("locomotives")]
    public async Task<IActionResult> Locomotives(CancellationToken ct) => Ok(await museum.Locomotives(ct));
    [HttpPost("locomotives"), Authorize(Policy = "Administration")]
    public async Task<IActionResult> CreateLocomotive(NameRequest request, CancellationToken ct) => Ok(await museum.CreateLocomotive(Actor, request.Name, ct));
    [HttpGet("me/competences")]
    public async Task<IActionResult> MyCompetences(CancellationToken ct) => Ok(await museum.Competences(Actor, ct));
    [HttpGet("members/{memberId:guid}/competences"), Authorize(Policy = "StaffRecords")]
    public async Task<IActionResult> Competences(Guid memberId, CancellationToken ct) => Ok(await museum.Competences(memberId, ct));
    [HttpPost("competences"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> RecordCompetence(CompetenceRequest request, CancellationToken ct) => Ok(await museum.RecordCompetence(Actor, request, ct));
    [HttpPost("competences/{id:guid}/revoke"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> Revoke(Guid id, ReasonRequest request, CancellationToken ct) => Ok(await museum.RevokeCompetence(Actor, id, request.Reason, ct));
    [HttpGet("me/training")]
    public async Task<IActionResult> MyTraining(CancellationToken ct) => Ok(await museum.Training(Actor, ct));
    [HttpGet("members/{memberId:guid}/training"), Authorize(Policy = "StaffRecords")]
    public async Task<IActionResult> Training(Guid memberId, CancellationToken ct) => Ok(await museum.Training(memberId, ct));
    [HttpPost("training"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> RecordTraining(TrainingRequest request, CancellationToken ct) => Ok(await museum.RecordTraining(Actor, request.MemberId, request.Date, request.Notes, ct));
    [HttpPost("duties"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> Duty(DutyRequest request, CancellationToken ct) => Ok(await museum.CreateDuty(Actor, request, ct));
    [HttpPost("duties/{id:guid}/assignment"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> Assign(Guid id, AssignRequest request, CancellationToken ct) => Ok(await museum.Assign(Actor, id, request.MemberId, ct));
    [HttpPost("assignments/{id:guid}/publish"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => Ok(await museum.SetAssignmentStatus(Actor, id, true, ct));
    [HttpPost("assignments/{id:guid}/cancel"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Ok(await museum.SetAssignmentStatus(Actor, id, false, ct));
    [HttpGet("roster"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> Roster(DateOnly from, DateOnly until, CancellationToken ct) => Ok(await museum.Roster(from, until, null, ct));
    [HttpGet("me/roster")]
    public async Task<IActionResult> MyRoster(DateOnly from, DateOnly until, CancellationToken ct) => Ok(await museum.Roster(from, until, Actor, ct));
    [HttpGet("audit"), Authorize(Policy = "Administration")]
    public async Task<IActionResult> Audit(DateTime sinceUtc, CancellationToken ct) => Ok(await museum.Audit(sinceUtc, ct));
}

[ApiController, Authorize(Policy = "Administration")]
[Route("api/accounts")]
public sealed class AccountsController(AccountService accounts) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest request, CancellationToken ct) => Ok(await accounts.Create(Actor, request, ct));
    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> Active(Guid id, ActiveRequest request, CancellationToken ct)
    { await accounts.SetActive(Actor, id, request.Active, ct); return NoContent(); }
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> Roles(Guid id, RolesRequest request, CancellationToken ct)
    { await accounts.SetRoles(Actor, id, request.Roles, ct); return NoContent(); }
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> Reset(Guid id, ResetPasswordRequest request, CancellationToken ct)
    { await accounts.ResetPassword(Actor, id, request.TemporaryPassword, ct); return NoContent(); }
}


