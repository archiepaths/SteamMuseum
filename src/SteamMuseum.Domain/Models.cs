namespace SteamMuseum.Domain;

public abstract class Entity { public Guid Id { get; set; } = Guid.NewGuid(); }
public enum DutyRole { Driver, Guard, Fireman, StationStaff }
public enum AvailabilityStatus { Available, Unavailable }
public enum WindowKind { Monthly, SpecialEvent }
public enum AssignmentStatus { Draft, Published, Cancelled }

public sealed class Member : Entity
{
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
}
public sealed class Railway : Entity { public string Name { get; set; } = ""; }
public sealed class Locomotive : Entity { public string Name { get; set; } = ""; }
public sealed class AvailabilityWindow : Entity
{
    public string Name { get; set; } = "";
    public WindowKind Kind { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public DateTime SubmissionDeadlineUtc { get; set; }
    public bool IsOpen { get; set; } = true;
    public bool Contains(DateOnly day) => day >= Start && day <= End;
}
// Shared across overlapping windows: one response per member and calendar date.
public sealed class DailyAvailability : Entity
{
    public Guid MemberId { get; set; }
    public DateOnly Date { get; set; }
    public AvailabilityStatus Status { get; set; }
    public TimeOnly? From { get; set; }
    public TimeOnly? Until { get; set; }
    public DutyRole? PreferredRole { get; set; }
    public string? Note { get; set; }
    public bool Covers(Duty duty) => Status == AvailabilityStatus.Available &&
        Date == duty.Date && (From is null || From <= duty.Start) && (Until is null || Until >= duty.End);
}
public sealed class WindowPreference : Entity
{
    public Guid MemberId { get; set; }
    public Guid WindowId { get; set; }
    public int? MaximumAssignments { get; set; }
}
// Assessment entries are never overwritten. Revocation retains the original evidence.
public sealed class Competence : Entity
{
    public Guid MemberId { get; set; }
    public DutyRole Role { get; set; }
    public Guid RailwayId { get; set; }
    public Guid? LocomotiveId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string Evidence { get; set; } = "";
    public Guid AssessedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
    public bool Qualifies(Duty duty) => RevokedAtUtc is null && Role == duty.Role &&
        RailwayId == duty.RailwayId && (LocomotiveId is null || LocomotiveId == duty.LocomotiveId) &&
        ValidFrom <= duty.Date && (ValidUntil is null || ValidUntil >= duty.Date);
}
public sealed class TrainingRecord : Entity
{
    public Guid MemberId { get; set; }
    public DateOnly Date { get; set; }
    public string Notes { get; set; } = "";
    public Guid RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
public sealed class Duty : Entity
{
    public string Name { get; set; } = "";
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public DutyRole Role { get; set; }
    public Guid RailwayId { get; set; }
    public Guid? LocomotiveId { get; set; }
    public bool Overlaps(Duty other) => Date == other.Date && Start < other.End && other.Start < End;
}
// One assignment row per duty; cancellation/reassignment is retained in the audit log.
public sealed class Assignment : Entity
{
    public Guid DutyId { get; set; }
    public Guid MemberId { get; set; }
    public AssignmentStatus Status { get; set; }
}
public sealed class AuditEntry : Entity
{
    public Guid ActorId { get; set; }
    public DateTime AtUtc { get; set; }
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

public static class RosterRules
{
    public static IReadOnlyList<string> Check(Duty duty, Member member, DailyAvailability? availability,
        IEnumerable<Competence> competences, IEnumerable<Duty> existingDuties,
        IEnumerable<(AvailabilityWindow Window, int? Maximum, int Assigned)> limits)
    {
        var issues = new List<string>();
        if (!member.Active) issues.Add("Member is inactive.");
        if (availability is null || !availability.Covers(duty)) issues.Add("Member is not available for the full duty.");
        if (!competences.Any(c => c.MemberId == member.Id && c.Qualifies(duty))) issues.Add("No valid competence for this role, locomotive and railway on the duty date.");
        if (existingDuties.Any(duty.Overlaps)) issues.Add("Member already has an overlapping duty.");
        foreach (var (window, maximum, assigned) in limits)
            if (window.Contains(duty.Date) && maximum.HasValue && assigned >= maximum.Value)
                issues.Add($"Maximum assignments reached for '{window.Name}'.");
        return issues;
    }
}
