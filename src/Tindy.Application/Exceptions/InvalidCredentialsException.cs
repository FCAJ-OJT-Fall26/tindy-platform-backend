
namespace Tindy.Application.Exceptions;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("The login credentials are incorrect.")
    {
    }
}
