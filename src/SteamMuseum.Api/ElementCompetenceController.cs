using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamMuseum.Application;

namespace SteamMuseum.Api;

public sealed record DutyCompetenceRoleRequest(Guid RoleId);
[ApiController, Route("api")]
public sealed class ElementCompetenceController(MuseumService museum) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("competence-elements")]
    public async Task<IActionResult> Elements(CancellationToken ct) => Ok(await museum.Elements(ct));
    [HttpPost("competence-elements"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> CreateElement(ElementRequest request, CancellationToken ct) => Ok(await museum.SaveElement(Actor, null, request, ct));
    [HttpPut("competence-elements/{id:guid}"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> UpdateElement(Guid id, ElementRequest request, CancellationToken ct) => Ok(await museum.SaveElement(Actor, id, request, ct));
    [HttpGet("competence-roles")]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await museum.CompetenceRoles(ct));
    [HttpPost("competence-roles"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> CreateRole(CompetenceRoleRequest request, CancellationToken ct) => Ok(await museum.SaveCompetenceRole(Actor, null, request, ct));
    [HttpPut("competence-roles/{id:guid}"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> UpdateRole(Guid id, CompetenceRoleRequest request, CancellationToken ct) => Ok(await museum.SaveCompetenceRole(Actor, id, request, ct));
    [HttpGet("me/element-assessments")]
    public async Task<IActionResult> Mine(CancellationToken ct) => Ok(await museum.Assessments(Actor, ct));
    [HttpGet("members/{memberId:guid}/element-assessments"), Authorize(Policy = "StaffRecords")]
    public async Task<IActionResult> Assessments(Guid memberId, CancellationToken ct) => Ok(await museum.Assessments(memberId, ct));
    [HttpPost("element-assessments"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> Assess(ElementAssessmentRequest request, CancellationToken ct) => Ok(await museum.AssessElement(Actor, request, ct));
    [HttpPost("element-assessments/{id:guid}/revoke"), Authorize(Policy = "Assessment")]
    public async Task<IActionResult> Revoke(Guid id, ReasonRequest request, CancellationToken ct) => Ok(await museum.RevokeAssessment(Actor, id, request.Reason, ct));
    [HttpGet("me/role-eligibility")]
    public async Task<IActionResult> MyEligibility(DateOnly? on, CancellationToken ct) => Ok(await museum.Eligibility(Actor, on, ct));
    [HttpGet("members/{memberId:guid}/role-eligibility"), Authorize(Policy = "StaffRecords")]
    public async Task<IActionResult> Eligibility(Guid memberId, DateOnly? on, CancellationToken ct) => Ok(await museum.Eligibility(memberId, on, ct));
    [HttpPut("duties/{id:guid}/competence-role"), Authorize(Policy = "Planning")]
    public async Task<IActionResult> DutyRole(Guid id, DutyCompetenceRoleRequest request, CancellationToken ct) => Ok(await museum.SetDutyCompetenceRole(Actor, id, request.RoleId, ct));
}
