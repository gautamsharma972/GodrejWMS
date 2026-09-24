namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Testable clock abstraction — handlers never call DateTimeOffset.UtcNow directly.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}
