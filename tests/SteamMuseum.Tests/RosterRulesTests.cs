using SteamMuseum.Domain;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class RosterRulesTests
{
    private readonly Member member = new();
    private readonly Guid railway = Guid.NewGuid();
    private readonly Guid locomotive = Guid.NewGuid();
    private Duty Duty() => new() { Date = new(2030, 10, 1), Start = new(10, 0), End = new(16, 0), Role = DutyRole.Driver, RailwayId = railway, LocomotiveId = locomotive };
    private Competence Qualification() => new() { MemberId = member.Id, Role = DutyRole.Driver, RailwayId = railway, LocomotiveId = locomotive, ValidFrom = new(2030, 1, 1), ValidUntil = new(2030, 10, 1) };
    private DailyAvailability Available() => new() { MemberId = member.Id, Date = new(2030, 10, 1), Status = AvailabilityStatus.Available };
    [Fact]
    public void Expiry_date_is_inclusive_and_preference_is_not_a_restriction()
    {
        var day = Available(); day.PreferredRole = DutyRole.Guard;
        Assert.Empty(RosterRules.Check(Duty(), member, day, [Qualification()], [], []));
    }
    [Theory]
    [InlineData("railway")]
    [InlineData("locomotive")]
    [InlineData("role")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("revoked")]
    [InlineData("other-member")]
    public void Invalid_qualification_cannot_authorize_assignment(string mismatch)
    {
        var qualification = Qualification();
        switch (mismatch) {
            case "railway": qualification.RailwayId = Guid.NewGuid(); break;
            case "locomotive": qualification.LocomotiveId = Guid.NewGuid(); break;
            case "role": qualification.Role = DutyRole.Guard; break;
            case "expired": qualification.ValidUntil = new(2030, 9, 30); break;
            case "future": qualification.ValidFrom = new(2030, 10, 2); break;
            case "revoked": qualification.RevokedAtUtc = DateTime.UtcNow; break;
            case "other-member": qualification.MemberId = Guid.NewGuid(); break;
        }
        Assert.Contains(RosterRules.Check(Duty(), member, Available(), [qualification], [], []), x => x.StartsWith("No valid competence"));
    }
    [Fact]
    public void Railway_wide_guard_competence_does_not_need_a_locomotive()
    {
        var duty = Duty(); duty.Role = DutyRole.Guard;
        var qualification = Qualification(); qualification.Role = DutyRole.Guard; qualification.LocomotiveId = null;
        Assert.Empty(RosterRules.Check(duty, member, Available(), [qualification], [], []));
    }
    [Fact]
    public void Missing_response_is_not_availability()
    { Assert.Contains(RosterRules.Check(Duty(), member, null, [Qualification()], [], []), x => x.Contains("not available")); }
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
        var issues = RosterRules.Check(Duty(), member, Available(), [Qualification()], [], [(monthly, 4, 4), (special, 3, 0)]);
        Assert.Single(issues); Assert.Contains("Month", issues[0]);
    }
    [Fact]
    public void Zero_limit_blocks_first_shift_and_blank_limit_is_unlimited()
    {
        var window = new AvailabilityWindow { Name = "Month", Start = new(2030, 10, 1), End = new(2030, 10, 31) };
        Assert.NotEmpty(RosterRules.Check(Duty(), member, Available(), [Qualification()], [], [(window, 0, 0)]));
        Assert.Empty(RosterRules.Check(Duty(), member, Available(), [Qualification()], [], [(window, null, 100)]));
    }
}
