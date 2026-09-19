using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SteamMuseum.Application;
using SteamMuseum.Domain;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Concurrent_planners_cannot_exceed_a_members_limit()
    {
        using var factory = new ApiFactory(); await factory.Seed();
        Guid first, second;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<MuseumService>();
            var ct = CancellationToken.None;
            var railway = await service.CreateRailway(factory.AdminId, "Railway", ct);
            var loco = await service.CreateLocomotive(factory.AdminId, "Loco", ct);
            var window = await service.CreateWindow(factory.AdminId, new("Month", WindowKind.Monthly, new(2030, 10, 1), new(2030, 10, 31), new(2030, 9, 30, 23, 0, 0, DateTimeKind.Utc)), ct);
            await service.RecordCompetence(factory.AdminId, new(factory.MemberId, DutyRole.Driver, railway.Id, loco.Id, new(2030, 1, 1), null, "Assessment"), ct);
            await service.SaveAvailability(factory.MemberId, window.Id, new(1, [new(new(2030, 10, 1), AvailabilityStatus.Available, null, null, null, null), new(new(2030, 10, 2), AvailabilityStatus.Available, null, null, null, null)]), ct);
            first = (await service.CreateDuty(factory.AdminId, new("First", new(2030, 10, 1), new(10, 0), new(16, 0), DutyRole.Driver, railway.Id, loco.Id), ct)).Id;
            second = (await service.CreateDuty(factory.AdminId, new("Second", new(2030, 10, 2), new(10, 0), new(16, 0), DutyRole.Driver, railway.Id, loco.Id), ct)).Id;
        }
        using var plannerA = factory.Client(); using var plannerB = factory.Client();
        await ApiFactory.Login(plannerA, "admin@example.test"); await ApiFactory.Login(plannerB, "admin@example.test");
        var results = await Task.WhenAll(
            plannerA.PostAsJsonAsync($"/api/duties/{first}/assignment", new { memberId = factory.MemberId }),
            plannerB.PostAsJsonAsync($"/api/duties/{second}/assignment", new { memberId = factory.MemberId }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        using var finalScope = factory.Services.CreateScope();
        var assignments = await finalScope.ServiceProvider.GetRequiredService<IStore>().List<Assignment>(x => x.Status != AssignmentStatus.Cancelled);
        Assert.Single(assignments);
    }
}
