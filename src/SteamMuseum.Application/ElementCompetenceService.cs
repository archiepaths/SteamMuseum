using SteamMuseum.Domain;

namespace SteamMuseum.Application;

public sealed partial class MuseumService
{
    public Task<List<CompetenceElement>> Elements(CancellationToken ct) => store.List<CompetenceElement>(_ => true, ct);
    public Task<List<CompetenceRole>> CompetenceRoles(CancellationToken ct) => store.List<CompetenceRole>(_ => true, ct);
    public Task<List<ElementAssessment>> Assessments(Guid memberId, CancellationToken ct) => store.List<ElementAssessment>(x => x.MemberId == memberId, ct);
    public Task<CompetenceElement> SaveElement(Guid actor, Guid? id, ElementRequest request, CancellationToken ct) => Change(actor, "CompetenceElementSaved", async () => {
        Require(Enum.IsDefined(request.LearningType), "Invalid learning type.");
        Require(request.ReassessmentMonths is >= 1 and <= 120, "Reassessment period must be 1 to 120 months.");
        Require(request.Description is not null && request.Description.Length <= 2000, "Description must be at most 2000 characters.");
        var item = id.HasValue ? await Get<CompetenceElement>(id.Value, ct) : new CompetenceElement();
        item.Name = Text(request.Name, 150, "Element name"); item.Description = request.Description!.Trim();
        item.LearningType = request.LearningType; item.ReassessmentMonths = request.ReassessmentMonths; item.Active = request.Active;
        if (!id.HasValue) store.Add(item);
        return item;
    }, ct);
    public Task<ElementAssessment> AssessElement(Guid actor, ElementAssessmentRequest request, CancellationToken ct) => Change(actor, "ElementAssessed", async () => {
        var member = await Get<Member>(request.MemberId, ct); Require(member.Active, "Member is inactive.");
        var element = await Get<CompetenceElement>(request.ElementId, ct); Require(element.Active, "Element is inactive.");
        Require(Enum.IsDefined(request.Outcome), "Invalid assessment outcome.");
        Require(request.AssessedOn.Year >= 1000 && request.AssessedOn <= DateOnly.FromDateTime(UtcNow), "Assessment date must not be in the future.");
        var history = await store.List<ElementAssessment>(x => x.MemberId == request.MemberId && x.ElementId == request.ElementId, ct);
        var item = new ElementAssessment { MemberId = member.Id, ElementId = element.Id, Outcome = request.Outcome,
            AssessedOn = request.AssessedOn, ReassessmentDue = request.AssessedOn.AddMonths(element.ReassessmentMonths),
            ReassessmentMonths = element.ReassessmentMonths, Sequence = history.Select(x => x.Sequence).DefaultIfEmpty().Max() + 1,
            Evidence = Text(request.Evidence, 2000, "Assessment evidence"), AssessedBy = actor, RecordedAtUtc = UtcNow };
        store.Add(item); return item;
    }, ct);
    public Task<ElementAssessment> RevokeAssessment(Guid actor, Guid id, string reason, CancellationToken ct) => Change(actor, "ElementAssessmentRevoked", async () => {
        var item = await Get<ElementAssessment>(id, ct); Require(item.RevokedAtUtc is null, "Assessment already revoked.", 409);
        item.RevokedAtUtc = UtcNow; item.RevocationReason = Text(reason, 500, "Reason"); return item;
    }, ct);
    public Task<CompetenceRole> SaveCompetenceRole(Guid actor, Guid? id, CompetenceRoleRequest request, CancellationToken ct) => Change(actor, "CompetenceRoleSaved", async () => {
        Require(Enum.IsDefined(request.Category), "Invalid role category.");
        Require(request.ElementIds is { Count: > 0 and <= 100 }, "Choose 1 to 100 required elements.");
        Require(request.ElementIds!.Distinct().Count() == request.ElementIds.Count, "Required elements must not be duplicated.");
        var item = id.HasValue ? await Get<CompetenceRole>(id.Value, ct) : new CompetenceRole();
        var category = request.Category; var railwayId = request.RailwayId;
        if (request.BaseRoleId is Guid baseId) {
            Require(baseId != id, "A role cannot inherit itself.");
            var parent = await Get<CompetenceRole>(baseId, ct);
            Require(parent.BaseRoleId is null && (parent.Active || !request.Active), "Select an active base role, not a variant.");
            category = parent.Category; railwayId = parent.RailwayId;
            Require(!parent.LocomotiveId.HasValue || parent.LocomotiveId == request.LocomotiveId, "Variant must retain its base role's locomotive scope.");
            Require(!parent.Requirements.Any(x => request.ElementIds.Contains(x.ElementId)), "Variant elements must be additional to the base role requirements.");
        }
        await Get<Railway>(railwayId, ct);
        if (request.LocomotiveId is Guid loco) await Get<Locomotive>(loco, ct);
        foreach (var elementId in request.ElementIds) {
            var element = await Get<CompetenceElement>(elementId, ct);
            Require(element.Active || item.Requirements.Any(r => r.ElementId == elementId), "New required elements must be active.");
        }
        if (id.HasValue) Require(item.BaseRoleId == request.BaseRoleId && item.Category == category && item.RailwayId == railwayId && item.LocomotiveId == request.LocomotiveId,
            "Role scope and base role cannot be changed. Create a new role or variant instead.");
        item.Name = Text(request.Name, 150, "Role name"); item.BaseRoleId = request.BaseRoleId;
        item.Category = category; item.RailwayId = railwayId; item.LocomotiveId = request.LocomotiveId; item.Active = request.Active;
        // Keep unchanged owned rows tracked; replacing all rows would duplicate composite keys.
        item.Requirements.RemoveAll(x => !request.ElementIds.Contains(x.ElementId));
        foreach (var elementId in request.ElementIds.Where(x => !item.Requirements.Any(r => r.ElementId == x))) item.Requirements.Add(new() { ElementId = elementId });
        if (!id.HasValue) store.Add(item);
        return item;
    }, ct);
    private async Task ValidateDutyRole(Guid roleId, DutyRole category, Guid railway, Guid? locomotive, CancellationToken ct)
    {
        var role = await Get<CompetenceRole>(roleId, ct);
        Require(role.Active && role.Category == category && role.RailwayId == railway && (!role.LocomotiveId.HasValue || role.LocomotiveId == locomotive), "Competence role does not match this duty's railway, category or locomotive.");
        if (role.BaseRoleId is Guid baseId) Require((await Get<CompetenceRole>(baseId, ct)).Active, "Base role is inactive.");
    }
    public Task<Duty> SetDutyCompetenceRole(Guid actor, Guid dutyId, Guid roleId, CancellationToken ct) => Change(actor, "DutyCompetenceRoleChanged", async () => {
        var duty = await Get<Duty>(dutyId, ct);
        await ValidateDutyRole(roleId, duty.Role, duty.RailwayId, duty.LocomotiveId, ct);
        duty.CompetenceRoleId = roleId; return duty;
    }, ct);
    private async Task<RoleEligibility> EvaluateRole(Guid memberId, Guid roleId, DateOnly date, CancellationToken ct)
    {
        var role = await Get<CompetenceRole>(roleId, ct);
        return ElementCompetenceRules.Evaluate(role, await CompetenceRoles(ct), await Elements(ct), await Assessments(memberId, ct), memberId, date);
    }
    public async Task<List<RoleEligibility>> Eligibility(Guid memberId, DateOnly? on, CancellationToken ct)
    {
        var member = await Get<Member>(memberId, ct);
        var date = on ?? DateOnly.FromDateTime(UtcNow);
        Require(date.Year >= 1000, "A valid date is required.");
        var roles = await CompetenceRoles(ct); var elements = await Elements(ct); var assessments = await Assessments(memberId, ct);
        return roles.Select(role => {
            var result = ElementCompetenceRules.Evaluate(role, roles, elements, assessments, memberId, date);
            if (!member.Active) return result with { Qualified = false, Issues = [.. result.Issues, "Member is inactive."] };
            return result;
        }).ToList();
    }
}

