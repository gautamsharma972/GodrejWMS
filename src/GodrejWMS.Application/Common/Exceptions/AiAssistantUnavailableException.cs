namespace GodrejWMS.Application.Common.Exceptions;

/// <summary>Thrown when the local AI assistant (Ollama) can't be reached or returns something
/// the app can't safely act on. Caught by the Web layer to show a friendly message instead of a
/// stack trace - the AI assistant is a convenience feature, not on the critical path.</summary>
public sealed class AiAssistantUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
