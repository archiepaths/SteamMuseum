using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SteamMuseum.Infrastructure;
using SteamMuseum.Application;
using SteamMuseum.Domain;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class ElementCompetenceTests
{
    [Fact]
    public async Task Removed_endpoints_and_unlinked_duties_cannot_use_old_qualification_rules()
    {
        using var factory = new ApiFactory(); await factory.Seed();
        using var admin = factory.Client(); await ApiFactory.Login(admin, "admin@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/me/competences")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/competences", new { })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MuseumService>();
        var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
        var ct = CancellationToken.None; var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var railway = await service.CreateRailway(factory.AdminId, "Test railway", ct);
        await Assert.ThrowsAsync<BusinessException>(() => service.CreateDuty(factory.AdminId, new("No role", today, new(9,0), new(12,0), DutyRole.Guard, railway.Id, null), ct));
        // Existing duty retained by migration: it is visible, but cannot be assigned or published.
        var duty = new Duty { Name = "Unlinked duty", Date = today, Start = new(9,0), End = new(12,0), Role = DutyRole.Guard, RailwayId = railway.Id };
        db.Add(duty); await db.SaveChangesAsync();
        var failure = await Assert.ThrowsAsync<BusinessException>(() => service.Assign(factory.AdminId, duty.Id, factory.MemberId, ct));
        Assert.Contains("Choose a competence role", failure.Message);
        var assignment = new Assignment { DutyId = duty.Id, MemberId = factory.MemberId, Status = AssignmentStatus.Draft };
        db.Add(assignment); await db.SaveChangesAsync();
        failure = await Assert.ThrowsAsync<BusinessException>(() => service.SetAssignmentStatus(factory.AdminId, assignment.Id, true, ct));
        Assert.Contains("Choose a competence role", failure.Message);
        Assert.Contains((await service.Roster(today, today, null, ct)).Single().Issues, x => x.Contains("Choose a competence role"));
    }
    [Fact]
    public async Task Retirement_migration_preserves_historical_rows_on_MySql()
    {
        if (Environment.GetEnvironmentVariable("MUSEUM_TEST_MYSQL") is null) return;
        using var factory = new ApiFactory(); await factory.Seed();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260923154343_AddElementCompetenceAndRoles");
        var railway = new Railway { Name = "Archive test" }; db.Add(railway); await db.SaveChangesAsync();
        var recordId = Guid.NewGuid().ToString(); var memberId = factory.MemberId.ToString(); var actorId = factory.AdminId.ToString(); var railwayId = railway.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Competence (Id, MemberId, Role, RailwayId, ValidFrom, Evidence, AssessedBy, RecordedAtUtc) VALUES ({recordId}, {memberId}, 1, {railwayId}, '2026-01-01', 'Historical evidence', {actorId}, '2026-01-01 12:00:00')");
        await migrator.MigrateAsync();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ArchivedCompetence WHERE Evidence = 'Historical evidence'").SingleAsync());
        await migrator.MigrateAsync("20260923154343_AddElementCompetenceAndRoles");
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Competence WHERE Evidence = 'Historical evidence'").SingleAsync());
        await migrator.MigrateAsync();
    }
    [Fact]
    public void Variant_requires_base_and_additional_elements_and_due_date_is_exclusive()
    {
        var member = Guid.NewGuid(); var date = new DateOnly(2026, 9, 23);
        var safety = new CompetenceElement { Name = "Safety" }; var loco = new CompetenceElement { Name = "Familiarisation" };
        var role = new CompetenceRole { Name = "Driver", Requirements = [new() { ElementId = safety.Id }] };
        var variant = new CompetenceRole { Name = "Driver B", BaseRoleId = role.Id, Requirements = [new() { ElementId = loco.Id }] };
        var pass = new ElementAssessment { MemberId = member, ElementId = safety.Id, Outcome = AssessmentOutcome.Competent, AssessedOn = date, ReassessmentDue = date.AddMonths(12), Sequence = 1 };
        var extra = new ElementAssessment { MemberId = member, ElementId = loco.Id, Outcome = AssessmentOutcome.Competent, AssessedOn = date, ReassessmentDue = date.AddMonths(6), Sequence = 1 };
        RoleEligibility Check(DateOnly day, params ElementAssessment[] history) => ElementCompetenceRules.Evaluate(variant, [role, variant], [safety, loco], history, member, day);
        Assert.False(Check(date, pass).Qualified);
        Assert.True(Check(date, pass, extra).Qualified);
        Assert.False(Check(date.AddMonths(6), pass, extra).Qualified);
        Assert.True(Check(date.AddMonths(6).AddDays(-1), pass, extra).Qualified);
        extra.MemberId = Guid.NewGuid(); Assert.False(Check(date, pass, extra).Qualified);
    }
    [Fact]
    public void Later_failure_or_revocation_never_revives_an_earlier_pass()
    {
        var member = Guid.NewGuid(); var date = new DateOnly(2026, 9, 23);
        var element = new CompetenceElement { Name = "Safety" };
        var role = new CompetenceRole { Requirements = [new() { ElementId = element.Id }] };
        var pass = new ElementAssessment { MemberId = member, ElementId = element.Id, Outcome = AssessmentOutcome.Competent, AssessedOn = date.AddDays(-1), ReassessmentDue = date.AddYears(1), Sequence = 1 };
        var fail = new ElementAssessment { MemberId = member, ElementId = element.Id, Outcome = AssessmentOutcome.NotCompetent, AssessedOn = date, ReassessmentDue = date.AddYears(1), Sequence = 2 };
        RoleEligibility Check(DateOnly day) => ElementCompetenceRules.Evaluate(role, [role], [element], [pass, fail], member, day);
        Assert.True(Check(date.AddDays(-1)).Qualified); Assert.False(Check(date).Qualified);
        fail.RevokedAtUtc = DateTime.UtcNow; Assert.False(Check(date).Qualified);
        fail.Outcome = AssessmentOutcome.Competent; fail.RevokedAtUtc = null; Assert.True(Check(date).Qualified);
        element.Active = false; Assert.False(Check(date).Qualified);
        element.Active = true; role.Active = false; Assert.False(Check(date).Qualified);
    }
    [Fact]
    public async Task Assessments_roles_variants_and_roster_checks_work_together()
    {
        using var factory = new ApiFactory(); await factory.Seed();
        var ct = CancellationToken.None; var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid safetyId, variantId, roleId, assessmentId, dutyId, railwayId, locoId, assignmentId;
        using (var scope = factory.Services.CreateScope()) {
            var s = scope.ServiceProvider.GetRequiredService<MuseumService>();
            railwayId = (await s.CreateRailway(factory.AdminId, "Railway A", ct)).Id;
            locoId = (await s.CreateLocomotive(factory.AdminId, "Locomotive B", ct)).Id;
            var safety = await s.SaveElement(factory.AdminId, null, new("Safety", "Theory assessment", LearningType.Theory, 12), ct); safetyId = safety.Id;
            var familiar = await s.SaveElement(factory.AdminId, null, new("B familiarisation", "Practical assessment", LearningType.Practical, 6), ct);
            var role = await s.SaveCompetenceRole(factory.AdminId, null, new("A driver", null, DutyRole.Driver, railwayId, null, [safety.Id]), ct); roleId = role.Id;
            var variant = await s.SaveCompetenceRole(factory.AdminId, null, new("A driver with B", role.Id, DutyRole.Driver, railwayId, locoId, [familiar.Id]), ct); variantId = variant.Id;
            var pass = await s.AssessElement(factory.AdminId, new(factory.MemberId, safety.Id, AssessmentOutcome.Competent, today, "Passed theory"), ct); assessmentId = pass.Id;
            await s.SaveElement(factory.AdminId, safety.Id, new("Safety", "Theory", LearningType.Theory, 24), ct);
            Assert.Equal(today.AddMonths(12), (await s.Assessments(factory.MemberId, ct)).Single().ReassessmentDue);
            var eligibility = await s.Eligibility(factory.MemberId, today, ct);
            Assert.True(eligibility.Single(x => x.RoleId == role.Id).Qualified);
            Assert.False(eligibility.Single(x => x.RoleId == variant.Id).Qualified);
            var window = await s.CreateWindow(factory.AdminId, new("Test", WindowKind.SpecialEvent, today, today.AddDays(1), DateTime.UtcNow.AddDays(2)), ct);
            await s.SaveAvailability(factory.MemberId, window.Id, new(null, [new(today.AddDays(1), AvailabilityStatus.Available, null, null, null, null)]), ct);
            var duty = await s.CreateDuty(factory.AdminId, new("Test duty", today.AddDays(1), new(9,0), new(12,0), DutyRole.Driver, railwayId, locoId, variant.Id), ct); dutyId = duty.Id;
            await Assert.ThrowsAsync<BusinessException>(() => s.Assign(factory.AdminId, duty.Id, factory.MemberId, ct));
            await s.AssessElement(factory.AdminId, new(factory.MemberId, familiar.Id, AssessmentOutcome.Competent, today, "Passed practical"), ct);
            assignmentId = (await s.Assign(factory.AdminId, duty.Id, factory.MemberId, ct)).Id;
            await s.AssessElement(factory.AdminId, new(factory.MemberId, safety.Id, AssessmentOutcome.NotCompetent, today, "Failed reassessment"), ct);
            await Assert.ThrowsAsync<BusinessException>(() => s.SetAssignmentStatus(factory.AdminId, assignmentId, true, ct));
            var roster = await s.Roster(today, today.AddDays(1), null, ct);
            Assert.Contains(roster.Single().Issues, i => i.Contains("Safety: Not competent"));
            var reassessed = await s.AssessElement(factory.AdminId, new(factory.MemberId, safety.Id, AssessmentOutcome.Competent, today, "Reassessed successfully"), ct);
            Assert.Equal(today.AddMonths(24), reassessed.ReassessmentDue);
            await s.SetAssignmentStatus(factory.AdminId, assignmentId, true, ct);
            var induction = await s.SaveElement(factory.AdminId, null, new("Induction", "Theory and practical", LearningType.TheoryAndPractical, 12), ct);
            await s.SaveCompetenceRole(factory.AdminId, role.Id, new("A driver", null, DutyRole.Driver, railwayId, null, [safety.Id, induction.Id]), ct);
            Assert.False((await s.Eligibility(factory.MemberId, today, ct)).Single(x => x.RoleId == variant.Id).Qualified);
            await s.AssessElement(factory.AdminId, new(factory.MemberId, induction.Id, AssessmentOutcome.Competent, today, "Induction passed"), ct);
            Assert.True((await s.Eligibility(factory.MemberId, today, ct)).Single(x => x.RoleId == variant.Id).Qualified);
        }
        // Read back through a fresh scope, proving requirements and assessments are persisted.
        using var member = factory.Client(); using var admin = factory.Client();
        await ApiFactory.Login(member, "member@example.test"); await ApiFactory.Login(admin, "admin@example.test");
        var current = await member.GetFromJsonAsync<JsonElement>("/api/me/role-eligibility");
        Assert.True(current.EnumerateArray().Single(x => x.GetProperty("roleId").GetGuid() == variantId).GetProperty("qualified").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/element-assessments", new { memberId = factory.MemberId, elementId = safetyId, outcome = "Competent", assessedOn = today, evidence = "Not allowed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/members/{factory.OtherId}/element-assessments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/competence-elements", new { name = "Not allowed", description = "", learningType = "Theory", reassessmentMonths = 12 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/duties", new { name = "Bypass", date = today, start = "09:00:00", end = "12:00:00", role = "Driver", railwayId, locomotiveId = locoId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/element-assessments", new { memberId = factory.MemberId, elementId = safetyId, outcome = "Competent", assessedOn = today.AddDays(1), evidence = "Future" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/competence-roles", new { name = "Empty", category = "Driver", railwayId, elementIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/competence-roles", new { name = "Nested variant", baseRoleId = variantId, category = "Driver", railwayId, elementIds = new[] { safetyId } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/competence-roles/{roleId}", new { name = "A driver updated", category = "Driver", railwayId, elementIds = new[] { safetyId }, active = false })).StatusCode);
        current = await member.GetFromJsonAsync<JsonElement>("/api/me/role-eligibility");
        Assert.False(current.EnumerateArray().Single(x => x.GetProperty("roleId").GetGuid() == variantId).GetProperty("qualified").GetBoolean());
    }
}
