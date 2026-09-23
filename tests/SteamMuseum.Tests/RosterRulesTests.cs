using SteamMuseum.Domain;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class RosterRulesTests
{
    private readonly Member member = new();
    private readonly Guid railway = Guid.NewGuid();
    private readonly Guid locomotive = Guid.NewGuid();
    private Duty Duty() => new() { Date = new(2030, 10, 1), Start = new(10, 0), End = new(16, 0), Role = DutyRole.Driver, RailwayId = railway, LocomotiveId = locomotive };
    private DailyAvailability Available() => new() { MemberId = member.Id, Date = new(2030, 10, 1), Status = AvailabilityStatus.Available };
    [Fact]
    public void Role_preference_is_not_a_restriction()
    {
        var day = Available(); day.PreferredRole = DutyRole.Guard;
        Assert.Empty(RosterRules.Check(Duty(), member, day, [], [], []));
    }
    [Fact]
    public void Element_issues_block_assignment()
    {
        Assert.Contains("Track safety: Reassessment due.", RosterRules.Check(Duty(), member, Available(), [], [], ["Track safety: Reassessment due."]));
    }
    [Fact]
    public void Missing_response_is_not_availability()
    { Assert.Contains(RosterRules.Check(Duty(), member, null, [], [], []), x => x.Contains("not available")); }
    [Fact]
    public void Adjacent_duties_do_not_overlap_but_partial_overlap_does()
    {
        var other = Duty(); other.Start = new(16, 0); other.End = new(17, 0);
        Assert.False(Duty().Overlaps(other)); other.Start = new(15, 59); Assert.True(Duty().Overlaps(other));
    }
    [Fact]
    public void Every_overlapping_window_limit_applies()
    {
        var monthly = new AvailabilityWindow { Name = "Month", Start = new(2030, 10, 1), End = new(2030, 10, 31) };
        var special = new AvailabilityWindow { Name = "Event", Start = new(2030, 10, 1), End = new(2030, 10, 1) };
        var issues = RosterRules.Check(Duty(), member, Available(), [], [(monthly, 4, 4), (special, 3, 0)], []);
        Assert.Single(issues); Assert.Contains("Month", issues[0]);
    }
    [Fact]
    public void Event_limit_does_not_apply_to_a_gap_between_ranges()
    {
        var window = new AvailabilityWindow { Name = "Split event", Start = new(2030, 9, 28), End = new(2030, 10, 10),
            DateRanges = [new() { Start = new(2030, 9, 28), End = new(2030, 9, 29) }, new() { Start = new(2030, 10, 9), End = new(2030, 10, 10) }] };
        Assert.False(window.Contains(Duty().Date));
        Assert.True(window.Contains(new(2030, 10, 9)));
        Assert.Empty(RosterRules.Check(Duty(), member, Available(), [], [(window, 0, 0)], []));
    }
    [Fact]
    public void Zero_limit_blocks_first_shift_and_blank_limit_is_unlimited()
    {
        var window = new AvailabilityWindow { Name = "Month", Start = new(2030, 10, 1), End = new(2030, 10, 31) };
        Assert.NotEmpty(RosterRules.Check(Duty(), member, Available(), [], [(window, 0, 0)], []));
        Assert.Empty(RosterRules.Check(Duty(), member, Available(), [], [(window, null, 100)], []));
    }
}
