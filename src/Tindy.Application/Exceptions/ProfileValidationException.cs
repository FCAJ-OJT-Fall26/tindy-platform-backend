namespace Tindy.Application.Exceptions;

public sealed class ProfileValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ProfileValidationException(IDictionary<string, string[]> errors)
        : base("The input data is invalid.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.Ordinal);
    }
}
