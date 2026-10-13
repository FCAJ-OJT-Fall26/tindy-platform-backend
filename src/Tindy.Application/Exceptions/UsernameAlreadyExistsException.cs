namespace Tindy.Application.Exceptions;

public sealed class UsernameAlreadyExistsException : ConflictException
{
    public UsernameAlreadyExistsException()
        : base("This username is already in use.") { }

    public UsernameAlreadyExistsException(Exception innerException)
        : base("This username is already in use.", innerException) { }
}
