using Handlezer.Api.Application;
using Handlezer.Api.Domain;
using Handlezer.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Handlezer.Tests;

/// <summary>
/// Tests voor AccessPolicyService: aanmaken van toegangsbeleid en evaluatie
/// van ECAL-regels ten opzichte van een tijdstip.
/// </summary>
public sealed class AccessPolicyServiceTests
{
    private static HandlezerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<HandlezerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateAsyncPersistsPolicyAndReturnsCorrectResponse()
    {
        // Een nieuw toegangsbeleid aanmaken: de service slaat het op in de database
        // en geeft de juiste waarden terug in het antwoord.
        await using var db = CreateDbContext();
        var service = new AccessPolicyService(db, new EcalRuleParser(), TimeProvider.System, new CapturingAuditWriter());

        var request = new AccessPolicyCreateRequest("Kantooruren", "allow; time=08:00..17:00; weekdays=Mon-Fri", "Europe/Amsterdam");

        var result = await service.CreateAsync(request, default);

        Assert.Equal("Kantooruren", result.Name);
        Assert.Equal("allow; time=08:00..17:00; weekdays=Mon-Fri", result.RuleText);
        Assert.Equal("Europe/Amsterdam", result.TimeZoneId);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task EvaluateAsyncThrowsKeyNotFoundWhenPolicyDoesNotExist()
    {
        // Als de opgegeven policyId niet bestaat, gooit de service een KeyNotFoundException.
        // De HTTP-laag vertaalt dit naar een 404-respons.
        await using var db = CreateDbContext();
        var service = new AccessPolicyService(db, new EcalRuleParser(), TimeProvider.System, new CapturingAuditWriter());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.EvaluateAsync(Guid.NewGuid(), new AccessPolicyEvaluateRequest(null, null, null), default));
    }

    [Fact]
    public async Task EvaluateAsyncAllowsAccessWhenTimestampFallsWithinPolicyWindow()
    {
        // Het tijdstip (donderdag 10:00 UTC) valt binnen het tijdvenster en de doordeweekse
        // weekdagen van de rule, dus de toegang moet worden toegestaan.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero); // donderdag
        var timeProvider = new FixedTimeProvider(fixedTime);
        var auditWriter = new CapturingAuditWriter();
        var service = new AccessPolicyService(db, new EcalRuleParser(), timeProvider, auditWriter);

        var policy = new AccessPolicy
        {
            Name = "Doordeweeks overdag",
            RuleText = "allow; time=08:00..17:00; weekdays=Mon-Fri",
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await service.EvaluateAsync(policy.Id, new AccessPolicyEvaluateRequest(null, null, null), default);

        Assert.True(result.Allowed);
        Assert.Equal(ScanOutcome.Allowed, auditWriter.Scans[0].Outcome);
    }

    [Fact]
    public async Task EvaluateAsyncDeniesAccessWhenTimestampIsOutsideTimeWindow()
    {
        // Het tijdstip (22:00 UTC) valt buiten het toegestane tijdvenster (08:00-17:00),
        // dus de toegang moet worden geweigerd ondanks dat het een doordeweekse dag is.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 22, 0, 0, TimeSpan.Zero); // donderdag, avond
        var timeProvider = new FixedTimeProvider(fixedTime);
        var auditWriter = new CapturingAuditWriter();
        var service = new AccessPolicyService(db, new EcalRuleParser(), timeProvider, auditWriter);

        var policy = new AccessPolicy
        {
            Name = "Kantooruren avond test",
            RuleText = "allow; time=08:00..17:00; weekdays=Mon-Fri",
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await service.EvaluateAsync(policy.Id, new AccessPolicyEvaluateRequest(null, null, null), default);

        Assert.False(result.Allowed);
        Assert.Equal(ScanOutcome.Denied, auditWriter.Scans[0].Outcome);
    }

    [Fact]
    public async Task EvaluateAsyncDeniesAccessWhenTimestampIsOutsideDateRange()
    {
        // Het tijdstip valt buiten het datumbereik van de rule, dus toegang wordt geweigerd
        // ook al ligt het tijdstip zelf binnen het dagvenster.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero); // buiten augustus
        var service = new AccessPolicyService(db, new EcalRuleParser(), new FixedTimeProvider(fixedTime), new CapturingAuditWriter());

        var policy = new AccessPolicy
        {
            Name = "Alleen augustus",
            RuleText = "allow; dates=2026-08-01..2026-08-31",
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await service.EvaluateAsync(policy.Id, new AccessPolicyEvaluateRequest(null, null, null), default);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task EvaluateAsyncDenyRuleAlwaysDeniesRegardlessOfTime()
    {
        // Een beleid met uitsluitend "deny" moet altijd toegang weigeren,
        // ongeacht het tijdstip, datum of weekdag.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var service = new AccessPolicyService(db, new EcalRuleParser(), new FixedTimeProvider(fixedTime), new CapturingAuditWriter());

        var policy = new AccessPolicy
        {
            Name = "Altijd geblokkeerd",
            RuleText = "deny",
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync();

        var result = await service.EvaluateAsync(policy.Id, new AccessPolicyEvaluateRequest(null, null, null), default);

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task EvaluateAsyncWritesAuditLogWithCorrectOutcome()
    {
        // Na elke evaluatie wordt er een audit-scan weggeschreven
        // met het juiste PolicyId en de bijbehorende uitkomst.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var auditWriter = new CapturingAuditWriter();
        var service = new AccessPolicyService(db, new EcalRuleParser(), new FixedTimeProvider(fixedTime), auditWriter);

        var policy = new AccessPolicy
        {
            Name = "Audit test",
            RuleText = "allow",
            TimeZoneId = "UTC",
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime
        };
        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync();

        await service.EvaluateAsync(policy.Id, new AccessPolicyEvaluateRequest(null, "device-99", null), default);

        Assert.Single(auditWriter.Scans);
        Assert.Equal(policy.Id, auditWriter.Scans[0].PolicyId);
        Assert.Equal("device-99", auditWriter.Scans[0].DeviceId);
        Assert.Equal(ScanKind.Access, auditWriter.Scans[0].Kind);
    }

    private sealed class CapturingAuditWriter : IAuditLogWriter
    {
        public List<AuditScan> Scans { get; } = [];

        public Task WriteAsync(AuditScan scan, CancellationToken cancellationToken)
        {
            Scans.Add(scan);
            return Task.CompletedTask;
        }
    }
}
