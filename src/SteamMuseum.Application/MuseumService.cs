using SteamMuseum.Domain;

namespace SteamMuseum.Application;

public sealed class MuseumService(IStore store, TimeProvider clock)
{
    public Task<List<AvailabilityWindow>> Windows(CancellationToken ct) => store.List<AvailabilityWindow>(_ => true, ct);
    public Task<List<Member>> Members(CancellationToken ct) => store.List<Member>(_ => true, ct);
    public Task<List<Railway>> Railways(CancellationToken ct) => store.List<Railway>(_ => true, ct);
    public Task<List<Locomotive>> Locomotives(CancellationToken ct) => store.List<Locomotive>(_ => true, ct);
    public Task<List<Competence>> Competences(Guid member, CancellationToken ct) => store.List<Competence>(x => x.MemberId == member, ct);
    public Task<List<TrainingRecord>> Training(Guid member, CancellationToken ct) => store.List<TrainingRecord>(x => x.MemberId == member, ct);
    public Task<List<AuditEntry>> Audit(DateTime sinceUtc, CancellationToken ct)
    {
        Require(sinceUtc.Kind == DateTimeKind.Utc && sinceUtc >= UtcNow.AddDays(-31), "Use a UTC audit start within the last 31 days.");
        return store.List<AuditEntry>(x => x.AtUtc >= sinceUtc, ct);
    }
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    private static void Require(bool condition, string message, int status = 400)
    { if (!condition) throw new BusinessException(message, status); }
    private static string Text(string? value, int length, string field)
    { Require(!string.IsNullOrWhiteSpace(value) && value.Length <= length, $"{field} is required and must be at most {length} characters."); return value!.Trim(); }
    private async Task<T> Get<T>(Guid id, CancellationToken ct) where T : Entity =>
        await store.Find<T>(id, ct) ?? throw new BusinessException($"{typeof(T).Name} was not found.", 404);
    private async Task<T> Change<T>(Guid actor, string action, Func<Task<T>> change, CancellationToken ct) =>
        await store.Write(async () => {
            var result = await change();
            store.Add(new AuditEntry { ActorId = actor, AtUtc = UtcNow, Action = action,
                Details = System.Text.Json.JsonSerializer.Serialize(result) });
            await store.Save(ct);
            return result;
        }, ct);

    public Task<Railway> CreateRailway(Guid actor, string name, CancellationToken ct) => Change(actor, "RailwayCreated", () => {
        var item = new Railway { Name = Text(name, 150, "Name") }; store.Add(item); return Task.FromResult(item);
    }, ct);
    public Task<Locomotive> CreateLocomotive(Guid actor, string name, CancellationToken ct) => Change(actor, "LocomotiveCreated", () => {
        var item = new Locomotive { Name = Text(name, 150, "Name") }; store.Add(item); return Task.FromResult(item);
    }, ct);
    public Task<AvailabilityWindow> CreateWindow(Guid actor, WindowRequest request, CancellationToken ct) => Change(actor, "WindowCreated", async () => {
        Require(Enum.IsDefined(request.Kind), "Invalid window kind.");
        Require(request.DateRanges is null || request.DateRanges.Count is > 0 and <= 367, "Supply between 1 and 367 date ranges.");
        Require(request.Kind == WindowKind.SpecialEvent || request.DateRanges is null, "Multiple date ranges are only supported for special events.");
        var ranges = request.DateRanges ?? [new WindowDateRange { Start = request.Start, End = request.End }];
        Require(ranges.All(r => r is not null && r.Start.Year >= 1000 && r.Start <= r.End), "Every date range must have valid dates in increasing order.");
        ranges = ranges.OrderBy(r => r.Start).ToList();
        var start = ranges[0].Start;
        var end = ranges[^1].End;
        Require(end.DayNumber - start.DayNumber <= 366, "Window must span at most 367 days.");
        for (var i = 1; i < ranges.Count; i++)
            Require(ranges[i].Start > ranges[i - 1].End, "Date ranges must not overlap.");
        Require(request.Notes is null || request.Notes.Length <= 2000, "Window notes must be at most 2000 characters.");
        Require(request.SubmissionDeadlineUtc.Kind == DateTimeKind.Utc, "Submission deadline must include UTC (Z).");
        if (request.Kind == WindowKind.Monthly)
        {
            Require(request.Start.Day == 1 && request.End == new DateOnly(request.Start.Year, request.Start.Month, DateTime.DaysInMonth(request.Start.Year, request.Start.Month)), "Monthly windows must cover exactly one calendar month.");
            Require(!(await store.List<AvailabilityWindow>(x => x.Kind == WindowKind.Monthly && x.Start == request.Start, ct)).Any(), "A monthly window already exists.", 409);
        }
        var item = new AvailabilityWindow { Name = Text(request.Name, 150, "Name"), Kind = request.Kind,
            Start = start, End = end, SubmissionDeadlineUtc = request.SubmissionDeadlineUtc,
            Notes = request.Notes?.Trim(), DateRanges = request.Kind == WindowKind.SpecialEvent ? ranges : [] };
        store.Add(item); return item;
    }, ct);
    public Task<AvailabilityWindow> SetWindowOpen(Guid actor, Guid id, bool open, DateTime deadlineUtc, CancellationToken ct) => Change(actor, "WindowStateChanged", async () => {
        Require(deadlineUtc.Kind == DateTimeKind.Utc, "Deadline must include UTC (Z).");
        var window = await Get<AvailabilityWindow>(id, ct); window.IsOpen = open; window.SubmissionDeadlineUtc = deadlineUtc; return window;
    }, ct);
    public Task<AvailabilityWindow> SetWindowNotes(Guid actor, Guid id, string? notes, CancellationToken ct) => Change(actor, "WindowNotesChanged", async () => {
        Require(notes is null || notes.Length <= 2000, "Window notes must be at most 2000 characters.");
        var window = await Get<AvailabilityWindow>(id, ct); window.Notes = notes?.Trim(); return window;
    }, ct);
    public async Task<AvailabilityView> Availability(Guid member, Guid windowId, CancellationToken ct)
    {
        var window = await Get<AvailabilityWindow>(windowId, ct);
        var preference = (await store.List<WindowPreference>(x => x.MemberId == member && x.WindowId == windowId, ct)).SingleOrDefault();
        var days = await store.List<DailyAvailability>(x => x.MemberId == member && x.Date >= window.Start && x.Date <= window.End, ct);
        var duties = await AssignedDuties(member, null, ct);
        return new(window, preference?.MaximumAssignments, duties.Count(x => window.Contains(x.Date)), days.Where(x => window.Contains(x.Date)).OrderBy(x => x.Date).ToList());
    }
    public Task<AvailabilityView> SaveAvailability(Guid member, Guid windowId, AvailabilityRequest request, CancellationToken ct) => Change(member, "AvailabilityUpdated", async () => {
        var person = await Get<Member>(member, ct); Require(person.Active, "Member is inactive.", 403);
        var window = await Get<AvailabilityWindow>(windowId, ct);
        Require(window.IsOpen && UtcNow <= window.SubmissionDeadlineUtc, "This availability window is closed.", 409);
        Require(request.MaximumAssignments is null or >= 0, "Maximum assignments cannot be negative.");
        Require(request.Days is not null && request.Days.Count <= 367, "Supply at most 367 dates.");
        var days = request.Days!;
        Require(days.Select(x => x.Date).Distinct().Count() == days.Count, "Dates must not be duplicated.");
        var assigned = await AssignedDuties(member, null, ct);
        Require(request.MaximumAssignments is null || request.MaximumAssignments >= assigned.Count(x => window.Contains(x.Date)),
            "The maximum cannot be lower than current assignments. Contact a planner to cancel duties first.", 409);
        foreach (var day in days)
        {
            Require(window.Contains(day.Date), "Every date must fall within the window.");
            Require(Enum.IsDefined(day.Status) && (day.PreferredRole is null || Enum.IsDefined(day.PreferredRole.Value)), "Invalid availability status or role.");
            Require((day.From is null && day.Until is null) || (day.From.HasValue && day.Until.HasValue && day.From < day.Until), "Supply both times in increasing order, or neither for a whole day.");
            Require(day.Status != AvailabilityStatus.Unavailable || (day.From is null && day.Until is null && day.PreferredRole is null), "Unavailable days cannot have times or role preferences.");
            Require(day.Note is null || day.Note.Length <= 500, "Notes must be at most 500 characters.");
            var entry = (await store.List<DailyAvailability>(x => x.MemberId == member && x.Date == day.Date, ct)).SingleOrDefault();
            if (entry is null) { entry = new() { MemberId = member, Date = day.Date }; store.Add(entry); }
            entry.Status = day.Status; entry.From = day.From; entry.Until = day.Until; entry.PreferredRole = day.PreferredRole; entry.Note = day.Note;
        }
        var preference = (await store.List<WindowPreference>(x => x.MemberId == member && x.WindowId == windowId, ct)).SingleOrDefault();
        if (preference is null) { preference = new() { MemberId = member, WindowId = windowId }; store.Add(preference); }
        preference.MaximumAssignments = request.MaximumAssignments;
        await store.Save(ct);
        return await Availability(member, windowId, ct);
    }, ct);
    public Task<Competence> RecordCompetence(Guid actor, CompetenceRequest request, CancellationToken ct) => Change(actor, "CompetenceRecorded", async () => {
        await Get<Member>(request.MemberId, ct); await ValidateScope(request.Role, request.RailwayId, request.LocomotiveId, ct);
        Require(request.ValidFrom.Year >= 1000 && (request.ValidUntil is null || request.ValidUntil >= request.ValidFrom), "Competence expiry precedes its start.");
        var item = new Competence { MemberId = request.MemberId, Role = request.Role, RailwayId = request.RailwayId,
            LocomotiveId = request.LocomotiveId, ValidFrom = request.ValidFrom, ValidUntil = request.ValidUntil,
            Evidence = Text(request.Evidence, 2000, "Assessment evidence"), AssessedBy = actor, RecordedAtUtc = UtcNow };
        store.Add(item); return item;
    }, ct);
    public Task<Competence> RevokeCompetence(Guid actor, Guid id, string reason, CancellationToken ct) => Change(actor, "CompetenceRevoked", async () => {
        var item = await Get<Competence>(id, ct); Require(item.RevokedAtUtc is null, "Competence is already revoked.", 409);
        item.RevocationReason = Text(reason, 500, "Reason"); item.RevokedAtUtc = UtcNow; return item;
    }, ct);
    public Task<TrainingRecord> RecordTraining(Guid actor, Guid member, DateOnly date, string notes, CancellationToken ct) => Change(actor, "TrainingRecorded", async () => {
        await Get<Member>(member, ct);
        Require(date.Year >= 1000, "A valid training date is required.");
        var item = new TrainingRecord { MemberId = member, Date = date, Notes = Text(notes, 2000, "Notes"), RecordedBy = actor, RecordedAtUtc = UtcNow };
        store.Add(item); return item;
    }, ct);
    private async Task ValidateScope(DutyRole role, Guid railway, Guid? locomotive, CancellationToken ct)
    {
        Require(Enum.IsDefined(role), "Invalid role."); await Get<Railway>(railway, ct);
        Require(role is not (DutyRole.Driver or DutyRole.Fireman) || locomotive.HasValue, "Driver and fireman qualifications/duties must specify a locomotive.");
        if (locomotive.HasValue) await Get<Locomotive>(locomotive.Value, ct);
    }
    public Task<Duty> CreateDuty(Guid actor, DutyRequest request, CancellationToken ct) => Change(actor, "DutyCreated", async () => {
        await ValidateScope(request.Role, request.RailwayId, request.LocomotiveId, ct);
        Require(request.Date.Year >= 1000 && request.Start < request.End, "Duty end must follow its start on the same day.");
        var item = new Duty { Name = Text(request.Name, 150, "Name"), Date = request.Date, Start = request.Start,
            End = request.End, Role = request.Role, RailwayId = request.RailwayId, LocomotiveId = request.LocomotiveId };
        store.Add(item); return item;
    }, ct);
    private Task<List<Duty>> AssignedDuties(Guid member, Guid? excludingDuty, CancellationToken ct) =>
        store.AssignedDuties(member, excludingDuty, ct);
    private async Task<IReadOnlyList<string>> Issues(Duty duty, Guid memberId, CancellationToken ct)
    {
        var member = await Get<Member>(memberId, ct);
        var availability = (await store.List<DailyAvailability>(x => x.MemberId == memberId && x.Date == duty.Date, ct)).SingleOrDefault();
        var competences = await Competences(memberId, ct);
        var assigned = await AssignedDuties(memberId, duty.Id, ct);
        var windows = await store.List<AvailabilityWindow>(x => x.Start <= duty.Date && x.End >= duty.Date, ct);
        var prefs = await store.List<WindowPreference>(x => x.MemberId == memberId, ct);
        return RosterRules.Check(duty, member, availability, competences, assigned,
            windows.Select(w => (w, prefs.SingleOrDefault(p => p.WindowId == w.Id)?.MaximumAssignments, assigned.Count(d => w.Contains(d.Date)))));
    }
    public Task<Assignment> Assign(Guid actor, Guid dutyId, Guid memberId, CancellationToken ct) => Change(actor, "DutyAssigned", async () => {
        var duty = await Get<Duty>(dutyId, ct);
        var assignment = (await store.List<Assignment>(x => x.DutyId == dutyId, ct)).SingleOrDefault();
        Require(assignment is null || assignment.Status == AssignmentStatus.Cancelled, "Cancel the existing assignment before reassigning this duty.", 409);
        var issues = await Issues(duty, memberId, ct); Require(issues.Count == 0, string.Join(" ", issues), 409);
        if (assignment is null) { assignment = new() { DutyId = dutyId }; store.Add(assignment); }
        assignment.MemberId = memberId; assignment.Status = AssignmentStatus.Draft; return assignment;
    }, ct);
    public Task<Assignment> SetAssignmentStatus(Guid actor, Guid id, bool publish, CancellationToken ct) => Change(actor, publish ? "AssignmentPublished" : "AssignmentCancelled", async () => {
        var assignment = await Get<Assignment>(id, ct);
        if (publish)
        {
            Require(assignment.Status != AssignmentStatus.Cancelled, "Cancelled assignments cannot be published.", 409);
            var issues = await Issues(await Get<Duty>(assignment.DutyId, ct), assignment.MemberId, ct);
            Require(issues.Count == 0, string.Join(" ", issues), 409);
        }
        assignment.Status = publish ? AssignmentStatus.Published : AssignmentStatus.Cancelled; return assignment;
    }, ct);
    public async Task<List<RosterView>> Roster(DateOnly from, DateOnly until, Guid? memberId, CancellationToken ct)
    {
        Require(from <= until && until.DayNumber - from.DayNumber <= 92, "Request a roster range of at most 93 days.");
        var duties = await store.List<Duty>(x => x.Date >= from && x.Date <= until, ct);
        var assignments = await store.AssignmentsInRange(from, until, ct);
        var result = new List<RosterView>();
        foreach (var duty in duties.OrderBy(x => x.Date).ThenBy(x => x.Start))
        {
            var assignment = assignments.SingleOrDefault(x => x.DutyId == duty.Id);
            if (memberId.HasValue && (assignment?.MemberId != memberId || assignment.Status != AssignmentStatus.Published)) continue;
            var issues = assignment is null ? Array.Empty<string>() : await Issues(duty, assignment.MemberId, ct);
            var availability = assignment is null ? null : (await store.List<DailyAvailability>(x => x.MemberId == assignment.MemberId && x.Date == duty.Date, ct)).SingleOrDefault();
            result.Add(new(duty, assignment, issues, availability?.PreferredRole));
        }
        return result;
    }
}


