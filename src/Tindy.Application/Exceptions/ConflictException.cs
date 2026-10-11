namespace Tindy.Application.Exceptions;

public class ConflictException : Exception
{
    public ConflictException()
        : base("The requested operation conflicts with the current state.")
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}