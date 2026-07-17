namespace ToBeClarify.Api.Exceptions;

public sealed class BusinessException : Exception
{
    public BusinessException(string message, string errorCode = "BUSINESS_ERROR")
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
