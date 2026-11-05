namespace Tindy.Application.Exceptions;

public sealed class ChatRequestException(int statusCode, string errorCode, string message, DateTime? retryAt = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public DateTime? RetryAt { get; } = retryAt;
}
