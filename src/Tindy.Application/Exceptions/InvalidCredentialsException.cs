
namespace Tindy.Application.Exceptions;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Thông tin đăng nhập không chính xác.")
    {
    }
}
