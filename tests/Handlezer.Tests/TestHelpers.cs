namespace Handlezer.Tests;

/// <summary>
/// Een TimeProvider die altijd hetzelfde vaste tijdstip teruggeeft.
/// Handig in tests waarbij we het tijdstip volledig willen beheersen.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedUtcNow;
}
