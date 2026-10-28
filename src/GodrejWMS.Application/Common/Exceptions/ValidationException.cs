using FluentValidation.Results;

namespace GodrejWMS.Application.Common.Exceptions;

/// <summary>Thrown by <see cref="Behaviours.ValidationBehaviour{TRequest,TResponse}"/> when a command/query fails FluentValidation.</summary>
public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this(failures.Where(failure => failure is not null).ToArray())
    {
    }

    private ValidationException(ValidationFailure[] failures)
        : base(BuildMessage(failures))
    {
        Errors = failures
            .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
    }

    private static string BuildMessage(IEnumerable<ValidationFailure> failures)
    {
        var messages = failures
            .Select(failure => failure.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        return messages.Length == 0
            ? "One or more validation failures have occurred."
            : $"Validation failed: {string.Join(" ", messages)}";
    }

    public IDictionary<string, string[]> Errors { get; }
}
