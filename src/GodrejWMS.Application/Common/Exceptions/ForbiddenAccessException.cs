namespace GodrejWMS.Application.Common.Exceptions;

/// <summary>The signed-in user lacks the permission or role an action requires. Derives from InvalidOperationException so
/// the existing pages (which show the message) keep working; the API maps it to 403.</summary>
public sealed class ForbiddenAccessException(string message) : InvalidOperationException(message);
