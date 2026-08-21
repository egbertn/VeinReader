using Handlezer.Api.Application;
using Handlezer.Api.Domain;
using Microsoft.Extensions.Options;
using Xunit;

namespace Handlezer.Tests;

public sealed class HandSignatureServiceTests
{
    [Fact]
    public async Task RegisterAndRecognizeReturnTheSamePerson()
    {
        var store = new InMemoryHandProfileStore();
        var auditWriter = new CapturingAuditWriter();
        var service = new HandSignatureService(
            store,
            auditWriter,
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions { StoreEnrollmentPhotos = false }),
            TimeProvider.System);

        var thumbprint = Convert.ToBase64String([1, 2, 3, 4, 5]);
        var registration = await service.RegisterAsync(new RegisterHandRequest("Ada Lovelace", new DateOnly(1815, 12, 10), thumbprint, null), default);

        var recognition = await service.RecognizeAsync(new RecognizeHandRequest(thumbprint, "device-1", null), default);

        Assert.True(recognition.IsMatch);
        Assert.Equal(registration.PersonId, recognition.PersonId);
        Assert.Equal("Ada Lovelace", recognition.FullName);
        Assert.Equal(2, auditWriter.Scans.Count);
    }

    [Fact]
    public async Task RecognizeReturnsNoMatchForUnknownThumbprint()
    {
        var store = new InMemoryHandProfileStore();
        var auditWriter = new CapturingAuditWriter();
        var service = new HandSignatureService(
            store,
            auditWriter,
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions()),
            TimeProvider.System);

        var recognition = await service.RecognizeAsync(new RecognizeHandRequest(Convert.ToBase64String([9, 9, 9]), null, null), default);

        Assert.False(recognition.IsMatch);
        Assert.Null(recognition.PersonId);
        Assert.Single(auditWriter.Scans);
    }

    [Fact]
    public async Task RegisterThrowsWhenThumbprintIsAlreadyRegistered()
    {
        // Dezelfde vingerafdruk mag slechts één keer worden geregistreerd;
        // een tweede poging gooit een InvalidOperationException.
        var store = new InMemoryHandProfileStore();
        var service = new HandSignatureService(
            store,
            new CapturingAuditWriter(),
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions()),
            TimeProvider.System);

        var thumbprint = Convert.ToBase64String([10, 20, 30]);
        await service.RegisterAsync(new RegisterHandRequest("Alice", new DateOnly(1990, 1, 1), thumbprint, null), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RegisterAsync(new RegisterHandRequest("Bob", new DateOnly(1995, 5, 5), thumbprint, null), default));
    }

    [Fact]
    public async Task RegisterStoresPhotoWhenOptionIsEnabled()
    {
        // Als StoreEnrollmentPhotos aan staat, wordt de foto opgeslagen
        // en geeft het resultaat PhotoStored = true terug.
        var store = new InMemoryHandProfileStore();
        var service = new HandSignatureService(
            store,
            new CapturingAuditWriter(),
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions { StoreEnrollmentPhotos = true }),
            TimeProvider.System);

        var result = await service.RegisterAsync(
            new RegisterHandRequest("Charlie", new DateOnly(2000, 3, 15),
                Convert.ToBase64String([7, 8, 9]),
                Convert.ToBase64String([255, 216, 255])),
            default);

        Assert.True(result.PhotoStored);
    }

    [Fact]
    public async Task RegisterIgnoresPhotoWhenOptionIsDisabled()
    {
        // Als StoreEnrollmentPhotos uit staat, wordt de foto niet opgeslagen,
        // ook als de aanroeper een foto meestuurt.
        var store = new InMemoryHandProfileStore();
        var service = new HandSignatureService(
            store,
            new CapturingAuditWriter(),
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions { StoreEnrollmentPhotos = false }),
            TimeProvider.System);

        var result = await service.RegisterAsync(
            new RegisterHandRequest("Dana", new DateOnly(1985, 7, 20),
                Convert.ToBase64String([11, 22, 33]),
                Convert.ToBase64String([255, 216, 255])),
            default);

        Assert.False(result.PhotoStored);
    }

    [Fact]
    public async Task RegisterThrowsArgumentExceptionForInvalidBase64Thumbprint()
    {
        // Een ongeldige base64-string als thumbprint leidt tot een ArgumentException
        // nog vóór de database wordt benaderd.
        var store = new InMemoryHandProfileStore();
        var service = new HandSignatureService(
            store,
            new CapturingAuditWriter(),
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions()),
            TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterAsync(new RegisterHandRequest("Eve", new DateOnly(1978, 11, 4), "not-valid-base64!!", null), default));
    }

    [Fact]
    public async Task RecognizeUsesExplicitTimestampWhenProvided()
    {
        // Als de aanroeper een OccurredAtUtc meegeeft, gebruikt de service dat tijdstip
        // in plaats van TimeProvider.GetUtcNow().
        var store = new InMemoryHandProfileStore();
        var auditWriter = new CapturingAuditWriter();
        var service = new HandSignatureService(
            store,
            auditWriter,
            new Sha256ThumbprintHasher(),
            Options.Create(new DataRetentionOptions()),
            TimeProvider.System);

        var thumbprint = Convert.ToBase64String([99, 88, 77]);
        var explicitTime = new DateTimeOffset(2025, 1, 15, 8, 30, 0, TimeSpan.Zero);

        var recognition = await service.RecognizeAsync(
            new RecognizeHandRequest(thumbprint, null, explicitTime), default);

        Assert.Equal(explicitTime, recognition.OccurredAtUtc);
        Assert.Equal(explicitTime, auditWriter.Scans[0].OccurredAtUtc);
    }

    private sealed class InMemoryHandProfileStore : IHandProfileStore
    {
        private readonly List<HandProfile> profiles = [];

        public Task<HandProfile?> FindByThumbprintHashAsync(string thumbprintHash, CancellationToken cancellationToken)
            => Task.FromResult(profiles.SingleOrDefault(item => item.ThumbprintHash == thumbprintHash));

        public Task AddAsync(HandProfile profile, CancellationToken cancellationToken)
        {
            profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
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