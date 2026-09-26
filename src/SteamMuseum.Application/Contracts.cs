using System.Linq.Expressions;
using SteamMuseum.Domain;

namespace SteamMuseum.Application;

public interface IStore
{
    Task<List<T>> List<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : Entity;
    Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity;
    void Add<T>(T entity) where T : Entity;
    Task Save(CancellationToken ct = default);
    Task<List<Duty>> AssignedDuties(Guid memberId, Guid? excludingDuty, CancellationToken ct);
    Task<List<Assignment>> AssignmentsInRange(DateOnly from, DateOnly until, CancellationToken ct);
    // Implementations must serialize all business mutations before reading relevant state.
    Task<T> Write<T>(Func<Task<T>> action, CancellationToken ct = default);
}
public sealed class BusinessException(string message, int status = 400) : Exception(message)
{ public int Status { get; } = status; }
public static class AccessRoles
{
    public const string Member = "Member", Planner = "Planner", Assessor = "Assessor", Administrator = "Administrator";
    public static readonly string[] All = [Member, Planner, Assessor, Administrator];
}
public sealed record WindowRequest(string Name, WindowKind Kind, DateOnly Start, DateOnly End, DateTime SubmissionDeadlineUtc, List<WindowDateRange>? DateRanges = null, string? Notes = null);
public sealed record DayRequest(DateOnly Date, AvailabilityStatus Status, TimeOnly? From, TimeOnly? Until, DutyRole? PreferredRole, string? Note);
public sealed record AvailabilityRequest(int? MaximumAssignments, List<DayRequest> Days);
public sealed record AvailabilityView(AvailabilityWindow Window, int? MaximumAssignments, int Assigned, List<DailyAvailability> Days, List<WindowAssignment> Assignments);
public sealed record DutyRequest(string Name, DateOnly Date, TimeOnly Start, TimeOnly End, DutyRole Role, Guid RailwayId, Guid? LocomotiveId, Guid? CompetenceRoleId = null);
public sealed record RosterView(Duty Duty, Assignment? Assignment, IReadOnlyList<string> Issues, DutyRole? PreferredRole);


public sealed record ElementRequest(string Name, string Description, LearningType LearningType, int ReassessmentMonths, bool Active = true);
public sealed record ElementAssessmentRequest(Guid MemberId, Guid ElementId, AssessmentOutcome Outcome, DateOnly AssessedOn, string Evidence);
public sealed record CompetenceRoleRequest(string Name, Guid? BaseRoleId, DutyRole Category, Guid RailwayId, Guid? LocomotiveId, List<Guid> ElementIds, bool Active = true);

public sealed record WindowAssignment(Guid DutyId, string Name, DateOnly Date, TimeOnly Start, TimeOnly End, string RoleName, AssignmentStatus Status);
