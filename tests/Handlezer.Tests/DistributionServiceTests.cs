using Handlezer.Api.Application;
using Handlezer.Api.Domain;
using Handlezer.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Handlezer.Tests;

/// <summary>
/// Tests voor DistributionService: aanmaken van uitgifte-policies en bewaken
/// van het dagelijks limiet per persoon.
/// </summary>
public sealed class DistributionServiceTests
{
    private static HandlezerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<HandlezerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateAsyncPersistsPolicyAndReturnsCorrectResponse()
    {
        // Een nieuwe uitgifte-policy aanmaken: de service slaat het op en
        // geeft naam, dagelijks limiet en tijdzone correct terug.
        await using var db = CreateDbContext();
        var service = new DistributionService(db, TimeProvider.System, new SilentAuditWriter());

        var result = await service.CreateAsync(
            new DistributionPolicyCreateRequest("Dagelijkse snack", 3, "Europe/Amsterdam"), default);

        Assert.Equal("Dagelijkse snack", result.Name);
        Assert.Equal(3, result.DailyLimit);
        Assert.Equal("Europe/Amsterdam", result.TimeZoneId);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task CreateAsyncClampsZeroOrNegativeDailyLimitToOne()
    {
        // Een dagelijks limiet van nul of negatief heeft geen zin; de service
        // corrigeert dit stilzwijgend naar 1 zodat de policy bruikbaar blijft.
        await using var db = CreateDbContext();
        var service = new DistributionService(db, TimeProvider.System, new SilentAuditWriter());

        var result = await service.CreateAsync(
            new DistributionPolicyCreateRequest("Zero limiet", 0, "UTC"), default);

        Assert.Equal(1, result.DailyLimit);
    }

    [Fact]
    public async Task ConsumeAsyncThrowsKeyNotFoundWhenPolicyDoesNotExist()
    {
        // Als de opgegeven policyId niet bestaat, gooit de service een KeyNotFoundException.
        // De HTTP-laag vertaalt dit naar een 404-respons.
        await using var db = CreateDbContext();
        var service = new DistributionService(db, TimeProvider.System, new SilentAuditWriter());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ConsumeAsync(Guid.NewGuid(), new DistributionConsumeRequest(Guid.NewGuid(), null, null), default));
    }

    [Fact]
    public async Task ConsumeAsyncAllowsFirstUseWhenUnderDailyLimit()
    {
        // Een persoon die vandaag nog niets verbruikt heeft, mag de policy consumeren.
        // Het resterend aantal is DailyLimit minus 1 na de huidige verbruiksronde.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var service = new DistributionService(db, new FixedTimeProvider(fixedTime), new SilentAuditWriter());

        var policy = new DistributionPolicy
        {
            Name = "Toegang limiet 2",
            DailyLimit = 2,
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.DistributionPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await service.ConsumeAsync(
            policy.Id, new DistributionConsumeRequest(Guid.NewGuid(), null, fixedTime), default);

        Assert.True(result.Allowed);
        Assert.Equal(1, result.RemainingToday);
    }

    [Fact]
    public async Task ConsumeAsyncDeniesWhenDailyLimitIsAlreadyReached()
    {
        // Als de persoon het dagelijks limiet al bereikt heeft (zichtbaar via eerdere
        // audit-scans), wordt verdere uitgifte geweigerd en is het resterende aantal 0.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var service = new DistributionService(db, new FixedTimeProvider(fixedTime), new SilentAuditWriter());
        var personId = Guid.NewGuid();

        var policy = new DistributionPolicy
        {
            Name = "Eenmalige uitgifte",
            DailyLimit = 1,
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.DistributionPolicies.Add(policy);

        // Bestaande toegestane scan voor dezelfde persoon vandaag simuleren
        db.AuditScans.Add(new AuditScan
        {
            PersonId = personId,
            PolicyId = policy.Id,
            Kind = ScanKind.Distribution,
            Outcome = ScanOutcome.Allowed,
            OccurredAtUtc = fixedTime
        });
        await db.SaveChangesAsync();

        var result = await service.ConsumeAsync(
            policy.Id, new DistributionConsumeRequest(personId, null, fixedTime), default);

        Assert.False(result.Allowed);
        Assert.Equal(0, result.RemainingToday);
    }

    [Fact]
    public async Task ConsumeAsyncDoesNotCountScansFromYesterday()
    {
        // Scans van een vorige dag tellen niet mee voor het daglijks limiet.
        // De persoon kan dus opnieuw consumeren op de nieuwe dag.
        await using var db = CreateDbContext();
        var yesterday = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var today = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var personId = Guid.NewGuid();

        var policy = new DistributionPolicy
        {
            Name = "Per dag één",
            DailyLimit = 1,
            TimeZoneId = "UTC",
            CreatedAtUtc = yesterday,
            UpdatedAtUtc = yesterday
        };
        var db2 = CreateDbContext(); // aparte context zodat de scan van gisteren al bestaat
        db2.DistributionPolicies.Add(policy);
        db2.AuditScans.Add(new AuditScan
        {
            PersonId = personId,
            PolicyId = policy.Id,
            Kind = ScanKind.Distribution,
            Outcome = ScanOutcome.Allowed,
            OccurredAtUtc = yesterday
        });
        await db2.SaveChangesAsync();

        var service = new DistributionService(db2, new FixedTimeProvider(today), new SilentAuditWriter());

        var result = await service.ConsumeAsync(
            policy.Id, new DistributionConsumeRequest(personId, null, today), default);

        Assert.True(result.Allowed);
    }

    private sealed class SilentAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditScan scan, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
