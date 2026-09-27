using SteamMuseum.Domain;
namespace SteamMuseum.Application;
public sealed partial class MuseumService
{
    public Task<AvailabilityWindow> UpdateWindow(Guid actor, Guid id, WindowUpdateRequest request, CancellationToken ct) => Change(actor, "WindowUpdated", async () => {
        var window = await Get<AvailabilityWindow>(id, ct);
        Require(!window.IsArchived, "Restore this window before editing it.", 409);
        Require(request.DeadlineUtc.Kind == DateTimeKind.Utc, "Submission deadline must include UTC (Z).");
        Require(request.Notes is null || request.Notes.Length <= 2000, "Window notes must be at most 2000 characters.");
        window.Name = Text(request.Name, 150, "Name");
        window.IsOpen = request.Open; window.SubmissionDeadlineUtc = request.DeadlineUtc; window.Notes = request.Notes?.Trim();
        return window;
    }, ct);
    public Task<List<AvailabilityWindow>> BulkWindows(Guid actor, WindowBulkRequest request, CancellationToken ct) => Change(actor, "WindowsBulkUpdated", async () => {
        Require(request.Ids is { Count: > 0 and <= 500 }, "Select between 1 and 500 windows.");
        Require(request.Action is "close" or "archive" or "restore", "Invalid window action.");
        var ids = request.Ids.Distinct().ToList();
        var windows = await store.List<AvailabilityWindow>(w => ids.Contains(w.Id), ct);
        Require(windows.Count == ids.Count, "A selected window no longer exists.", 404);
        foreach (var window in windows) {
            window.IsOpen = false;
            if (request.Action == "archive") window.IsArchived = true;
            if (request.Action == "restore") window.IsArchived = false;
        }
        return windows;
    }, ct);
    public async Task<WindowMatrix> WindowAvailability(Guid id, CancellationToken ct) {
        var window = await Get<AvailabilityWindow>(id, ct);
        var days = (await store.List<DailyAvailability>(d => d.Date >= window.Start && d.Date <= window.End, ct)).Where(d => window.Contains(d.Date)).ToList();
        var ids = days.Select(d => d.MemberId).Distinct().ToList();
        var members = await store.List<Member>(m => ids.Contains(m.Id), ct);
        var grouped = days.ToLookup(d => d.MemberId);
        return new(window, members.OrderBy(m => m.DisplayName).Select(m => new WindowMatrixMember(m.Id, m.DisplayName, grouped[m.Id].OrderBy(d => d.Date).ToList())).ToList());
    }
}
