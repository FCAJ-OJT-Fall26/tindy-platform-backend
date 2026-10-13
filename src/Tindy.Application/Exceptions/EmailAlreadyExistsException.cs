
namespace Tindy.Application.Exceptions;

public sealed class EmailAlreadyExistsException
    : ConflictException
{
    public EmailAlreadyExistsException()
        : base("This email address is already in use.")
    {
    }

    public EmailAlreadyExistsException(Exception innerException)
        : base("This email address is already in use.", innerException)
    {
    }
}
