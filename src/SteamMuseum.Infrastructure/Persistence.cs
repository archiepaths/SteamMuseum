using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenIddict.EntityFrameworkCore.Models;
using SteamMuseum.Application;
using SteamMuseum.Domain;

namespace SteamMuseum.Infrastructure;

public sealed class MuseumUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; } = true;
}
public sealed class MutationLock { public int Id { get; set; } public long Revision { get; set; } }

public sealed class MuseumDbContext(DbContextOptions<MuseumDbContext> options) : IdentityDbContext<MuseumUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        base.ConfigureConventions(b);
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        // Connector/NET's reader returns DateTime/TimeSpan; explicit converters preserve DateOnly/TimeOnly in the domain.
        b.Properties<DateOnly>().HaveConversion<CalendarDateConverter>().HaveColumnType("date");
        b.Properties<TimeOnly>().HaveConversion<LocalTimeConverter>().HaveColumnType("time(6)");
    }
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.UseOpenIddict();
        // Subjects are the existing GUID member IDs; keep MySQL composite indexes below 3072 bytes.
        b.Entity<OpenIddictEntityFrameworkCoreAuthorization>().Property(x => x.Subject).HasMaxLength(36);
        b.Entity<OpenIddictEntityFrameworkCoreToken>().Property(x => x.Subject).HasMaxLength(36);
        b.Entity<Member>().Property(x => x.DisplayName).HasMaxLength(150);
        b.Entity<Member>().HasOne<MuseumUser>().WithOne().HasForeignKey<Member>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Railway>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<Locomotive>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<AvailabilityWindow>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<AvailabilityWindow>().Property(x => x.Notes).HasMaxLength(2000);
        b.Entity<AvailabilityWindow>().OwnsMany(x => x.DateRanges, ranges => {
            ranges.ToTable("AvailabilityWindowDateRange");
            ranges.WithOwner().HasForeignKey("WindowId");
            ranges.HasKey("WindowId", nameof(WindowDateRange.Start));
        });
        b.Entity<DailyAvailability>().HasIndex(x => new { x.MemberId, x.Date }).IsUnique();
        b.Entity<DailyAvailability>().Property(x => x.Note).HasMaxLength(500);
        b.Entity<DailyAvailability>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WindowPreference>().HasIndex(x => new { x.MemberId, x.WindowId }).IsUnique();
        b.Entity<WindowPreference>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WindowPreference>().HasOne<AvailabilityWindow>().WithMany().HasForeignKey(x => x.WindowId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CompetenceElement>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<CompetenceElement>().Property(x => x.Description).HasMaxLength(2000);
        b.Entity<CompetenceRole>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<CompetenceRole>().HasOne<CompetenceRole>().WithMany().HasForeignKey(x => x.BaseRoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CompetenceRole>().HasOne<Railway>().WithMany().HasForeignKey(x => x.RailwayId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CompetenceRole>().HasOne<Locomotive>().WithMany().HasForeignKey(x => x.LocomotiveId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CompetenceRole>().OwnsMany(x => x.Requirements, r => {
            r.ToTable("RoleElement"); r.WithOwner().HasForeignKey("RoleId"); r.HasKey("RoleId", nameof(RoleElement.ElementId));
            r.HasOne<CompetenceElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ElementAssessment>().Property(x => x.Evidence).HasMaxLength(2000);
        b.Entity<ElementAssessment>().Property(x => x.RevocationReason).HasMaxLength(500);
        b.Entity<ElementAssessment>().HasIndex(x => new { x.MemberId, x.ElementId, x.Sequence }).IsUnique();
        b.Entity<ElementAssessment>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementAssessment>().HasOne<Member>().WithMany().HasForeignKey(x => x.AssessedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ElementAssessment>().HasOne<CompetenceElement>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Duty>().HasOne<CompetenceRole>().WithMany().HasForeignKey(x => x.CompetenceRoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrainingRecord>().Property(x => x.Notes).HasMaxLength(2000);
        b.Entity<TrainingRecord>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TrainingRecord>().HasOne<Member>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Duty>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<Duty>().HasIndex(x => x.Date);
        b.Entity<Duty>().HasOne<Railway>().WithMany().HasForeignKey(x => x.RailwayId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Duty>().HasOne<Locomotive>().WithMany().HasForeignKey(x => x.LocomotiveId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assignment>().HasIndex(x => x.DutyId).IsUnique();
        b.Entity<Assignment>().HasIndex(x => new { x.MemberId, x.Status });
        b.Entity<Assignment>().HasOne<Member>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Assignment>().HasOne<Duty>().WithOne().HasForeignKey<Assignment>(x => x.DutyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEntry>().Property(x => x.Action).HasMaxLength(80);
        b.Entity<AuditEntry>().HasIndex(x => x.AtUtc);
        b.Entity<MutationLock>().HasData(new MutationLock { Id = 1, Revision = 0 });
    }
}

public sealed class EfStore(MuseumDbContext db) : IStore
{
    public Task<List<T>> List<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : Entity => db.Set<T>().Where(predicate).ToListAsync(ct);
    public async Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity => await db.Set<T>().FindAsync([id], ct);
    public void Add<T>(T entity) where T : Entity => db.Set<T>().Add(entity);
    public async Task Save(CancellationToken ct = default) => await db.SaveChangesAsync(ct);
    public Task<List<Duty>> AssignedDuties(Guid memberId, Guid? excludingDuty, CancellationToken ct) =>
        (from assignment in db.Set<Assignment>()
         join duty in db.Set<Duty>() on assignment.DutyId equals duty.Id
         where assignment.MemberId == memberId && assignment.Status != AssignmentStatus.Cancelled && assignment.DutyId != excludingDuty
         select duty).ToListAsync(ct);
    public Task<List<Assignment>> AssignmentsInRange(DateOnly startDate, DateOnly until, CancellationToken ct) =>
        (from assignment in db.Set<Assignment>()
         join duty in db.Set<Duty>() on assignment.DutyId equals duty.Id
         where assignment.Status != AssignmentStatus.Cancelled && duty.Date >= startDate && duty.Date <= until
         select assignment).ToListAsync(ct);
    public async Task<T> Write<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // Database-wide row lock, shared across API instances. Acquire before ANY business reads.
        // This modest-throughput museum API favours correctness over parallel mutation throughput.
        var affected = await db.Set<MutationLock>().Where(x => x.Id == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, x => x.Revision + 1), ct);
        if (affected != 1) throw new InvalidOperationException("Database has not been initialized with its mutation lock.");
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }
}


public sealed class CalendarDateConverter() : ValueConverter<DateOnly, DateTime>(
    value => value.ToDateTime(TimeOnly.MinValue), value => DateOnly.FromDateTime(value));
public sealed class LocalTimeConverter() : ValueConverter<TimeOnly, TimeSpan>(
    value => value.ToTimeSpan(), value => TimeOnly.FromTimeSpan(value));



public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
