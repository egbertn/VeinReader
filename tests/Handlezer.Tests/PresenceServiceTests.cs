using Handlezer.Api.Application;
using Handlezer.Api.Domain;
using Handlezer.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Handlezer.Tests;

/// <summary>
/// Tests voor PresenceService: inchecken en uitchecken van personen,
/// inclusief de grenssituaties zoals dubbel inchecken en uitchecken zonder sessie.
/// </summary>
public sealed class PresenceServiceTests
{
    private static HandlezerDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<HandlezerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CheckInAsyncCreatesNewSessionForPerson()
    {
        // Als er nog geen open sessie is, maakt de service een nieuwe aan
        // en geeft Successful = true terug.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(fixedTime), new SilentAuditWriter());
        var personId = Guid.NewGuid();

        var result = await service.CheckInAsync(
            new PresenceCheckInRequest(personId, "device-1", fixedTime), default);

        Assert.True(result.Successful);
        Assert.Equal("Check-in recorded.", result.Reason);
        Assert.Single(db.PresenceSessions.ToList());
    }

    [Fact]
    public async Task CheckInAsyncReturnsFalseWhenPersonIsAlreadyCheckedIn()
    {
        // Een persoon met een al open sessie kan niet nogmaals inchecken.
        // De service geeft Successful = false en de reden terug zonder een nieuwe sessie aan te maken.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(fixedTime), new SilentAuditWriter());
        var personId = Guid.NewGuid();

        await service.CheckInAsync(new PresenceCheckInRequest(personId, null, fixedTime), default);
        var result = await service.CheckInAsync(new PresenceCheckInRequest(personId, null, fixedTime), default);

        Assert.False(result.Successful);
        Assert.Equal("Person is already checked in.", result.Reason);
        Assert.Single(db.PresenceSessions.ToList()); // nog steeds slechts één sessie
    }

    [Fact]
    public async Task CheckOutAsyncClosesOpenSessionAndSetsCheckOutTime()
    {
        // Na een succesvolle check-in kan de persoon uitchecken;
        // de sessie krijgt de opgegeven uitchecktijd mee.
        await using var db = CreateDbContext();
        var checkInTime = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var checkOutTime = new DateTimeOffset(2026, 8, 20, 17, 0, 0, TimeSpan.Zero);
        var service = new PresenceService(db, new FixedTimeProvider(checkInTime), new SilentAuditWriter());
        var personId = Guid.NewGuid();

        await service.CheckInAsync(new PresenceCheckInRequest(personId, "device-1", checkInTime), default);
        var result = await service.CheckOutAsync(
            new PresenceCheckOutRequest(personId, "device-1", checkOutTime), default);

        Assert.True(result.Successful);
        Assert.Equal("Check-out recorded.", result.Reason);

        var session = db.PresenceSessions.Single();
        Assert.Equal(checkOutTime, session.CheckedOutAtUtc);
    }

    [Fact]
    public async Task CheckOutAsyncReturnsFalseWhenNoOpenSessionExists()
    {
        // Uitchecken zonder een voorafgaande check-in mislukt; de service geeft
        // Successful = false en legt een audit-scan vast met Outcome NoOpenSession.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 17, 0, 0, TimeSpan.Zero);
        var auditWriter = new CapturingAuditWriter();
        var service = new PresenceService(db, new FixedTimeProvider(fixedTime), auditWriter);
        var personId = Guid.NewGuid();

        var result = await service.CheckOutAsync(
            new PresenceCheckOutRequest(personId, null, fixedTime), default);

        Assert.False(result.Successful);
        Assert.Equal("No open check-in session was found.", result.Reason);
        Assert.Equal(ScanOutcome.NoOpenSession, auditWriter.Scans[0].Outcome);
    }

    [Fact]
    public async Task CheckInAsyncWritesAuditLogWithStartedOutcome()
    {
        // Bij een succesvolle check-in wordt een audit-scan met Outcome = Started vastgelegd.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        var auditWriter = new CapturingAuditWriter();
        var service = new PresenceService(db, new FixedTimeProvider(fixedTime), auditWriter);
        var personId = Guid.NewGuid();

        await service.CheckInAsync(new PresenceCheckInRequest(personId, "poort-A", fixedTime), default);

        Assert.Single(auditWriter.Scans);
        Assert.Equal(personId, auditWriter.Scans[0].PersonId);
        Assert.Equal(ScanKind.PresenceCheckIn, auditWriter.Scans[0].Kind);
        Assert.Equal(ScanOutcome.Started, auditWriter.Scans[0].Outcome);
    }

    [Fact]
    public async Task CheckOutAsyncWritesAuditLogWithCompletedOutcome()
    {
        // Bij een succesvolle check-out wordt een audit-scan met Outcome = Completed vastgelegd.
        await using var db = CreateDbContext();
        var fixedTime = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        var checkOutTime = fixedTime.AddHours(8);
        var auditWriter = new CapturingAuditWriter();
        var service = new PresenceService(db, new FixedTimeProvider(fixedTime), auditWriter);
        var personId = Guid.NewGuid();

        await service.CheckInAsync(new PresenceCheckInRequest(personId, null, fixedTime), default);
        auditWriter.Scans.Clear(); // reset zodat we alleen de check-out scan beoordelen

        await service.CheckOutAsync(new PresenceCheckOutRequest(personId, null, checkOutTime), default);

        Assert.Single(auditWriter.Scans);
        Assert.Equal(ScanKind.PresenceCheckOut, auditWriter.Scans[0].Kind);
        Assert.Equal(ScanOutcome.Completed, auditWriter.Scans[0].Outcome);
    }

    private sealed class SilentAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditScan scan, CancellationToken cancellationToken) => Task.CompletedTask;
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
