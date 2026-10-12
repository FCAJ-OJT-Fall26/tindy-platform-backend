
namespace Tindy.Application.Exceptions;

public sealed class EmailAlreadyExistsException
    : ConflictException
{
    public EmailAlreadyExistsException()
        : base("Email này đã được sử dụng.")
    {
    }

    public EmailAlreadyExistsException(Exception innerException)
        : base(
            "Email này đã được sử dụng.",
            innerException)
    {
    }
}
