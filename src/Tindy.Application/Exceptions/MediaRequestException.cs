namespace Tindy.Application.Exceptions;

public sealed class MediaRequestException : Exception
{
    public MediaRequestException(int statusCode, string errorCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
    public int StatusCode { get; }
    public string ErrorCode { get; }
}
