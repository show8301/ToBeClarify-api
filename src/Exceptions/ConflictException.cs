namespace ToBeClarify.Api.Exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(string message, string errorCode = "CONFLICT") : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
