namespace ToBeClarify.Api.Exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message, string errorCode = "NOT_FOUND") : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
