namespace Tindy.Application.Exceptions;

public sealed class UsernameAlreadyExistsException : ConflictException
{
    public UsernameAlreadyExistsException()
        : base("Username đã được sử dụng.") { }

    public UsernameAlreadyExistsException(Exception innerException)
        : base("Username đã được sử dụng.", innerException) { }
}
