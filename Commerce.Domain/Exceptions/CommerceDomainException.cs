namespace Commerce.Domain.Exceptions;

public class CommerceDomainException : DomainException
{
    public CommerceDomainException(string errorCode, string message, int statusCode = 400)
        : base(errorCode, message, statusCode)
    {
    }
}
