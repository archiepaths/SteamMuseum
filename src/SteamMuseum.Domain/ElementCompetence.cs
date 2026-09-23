namespace SteamMuseum.Domain;

public enum LearningType { Theory, Practical, TheoryAndPractical }
public enum AssessmentOutcome { Competent, NotCompetent }
public sealed class CompetenceElement : Entity
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public LearningType LearningType { get; set; }
    public int ReassessmentMonths { get; set; } = 12;
    public bool Active { get; set; } = true;
}
public sealed class ElementAssessment : Entity
{
    public Guid MemberId { get; set; }
    public Guid ElementId { get; set; }
    public AssessmentOutcome Outcome { get; set; }
    public DateOnly AssessedOn { get; set; }
    // Reassessment is due on this date: competence is valid strictly before it.
    public DateOnly ReassessmentDue { get; set; }
    public int ReassessmentMonths { get; set; }
    public long Sequence { get; set; }
    public string Evidence { get; set; } = "";
    public Guid AssessedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
}
public sealed class CompetenceRole : Entity
{
    public string Name { get; set; } = "";
    public Guid? BaseRoleId { get; set; }
    public DutyRole Category { get; set; }
    public Guid RailwayId { get; set; }
    public Guid? LocomotiveId { get; set; }
    public bool Active { get; set; } = true;
    public List<RoleElement> Requirements { get; set; } = [];
}
public sealed class RoleElement { public Guid ElementId { get; set; } }
public sealed record ElementStatus(Guid ElementId, string Name, string Status, DateOnly? ReassessmentDue);
public sealed record RoleEligibility(Guid RoleId, string Name, bool Qualified, List<ElementStatus> Elements, List<string> Issues);

public static class ElementCompetenceRules
{
    public static RoleEligibility Evaluate(CompetenceRole role, IReadOnlyList<CompetenceRole> roles,
        IReadOnlyList<CompetenceElement> elements, IEnumerable<ElementAssessment> assessments, Guid memberId, DateOnly date)
    {
        var issues = new List<string>();
        var required = role.Requirements.Select(x => x.ElementId).ToHashSet();
        if (!role.Active) issues.Add("Role is inactive.");
        if (role.BaseRoleId is Guid baseId)
        {
            var parent = roles.SingleOrDefault(x => x.Id == baseId);
            if (parent is null || parent.BaseRoleId is not null) issues.Add("Base role is invalid.");
            else {
                if (!parent.Active) issues.Add("Base role is inactive.");
                required.UnionWith(parent.Requirements.Select(x => x.ElementId));
            }
        }
        if (required.Count == 0) issues.Add("Role has no required competence elements.");
        var statuses = new List<ElementStatus>();
        foreach (var id in required.Order())
        {
            var element = elements.SingleOrDefault(x => x.Id == id);
            var latest = assessments.Where(x => x.MemberId == memberId && x.ElementId == id && x.AssessedOn <= date)
                .OrderByDescending(x => x.AssessedOn).ThenByDescending(x => x.Sequence).FirstOrDefault();
            var status = element is null || !element.Active ? "Inactive element"
                : latest is null ? "Not assessed"
                : latest.RevokedAtUtc is not null ? "Revoked"
                : latest.Outcome == AssessmentOutcome.NotCompetent ? "Not competent"
                : latest.ReassessmentDue <= date ? "Reassessment due" : "Competent";
            var name = element?.Name ?? "Unknown element";
            statuses.Add(new(id, name, status, latest?.ReassessmentDue));
            if (status != "Competent") issues.Add($"{name}: {status}.");
        }
        return new(role.Id, role.Name, issues.Count == 0, statuses, issues);
    }
}
