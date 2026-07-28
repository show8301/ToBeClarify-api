namespace ToBeClarify.Api.Exceptions;

public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message = "Forbidden", string errorCode = "FORBIDDEN") : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
