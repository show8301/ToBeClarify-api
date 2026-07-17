namespace ToBeClarify.Api.Exceptions;

public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Unauthorized", string errorCode = "UNAUTHORIZED")
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
