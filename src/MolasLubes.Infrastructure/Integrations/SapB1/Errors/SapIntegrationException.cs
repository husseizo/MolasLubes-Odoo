namespace MolasLubes.Infrastructure.Integrations.SapB1.Errors;

public class SapIntegrationException : Exception
{
    public string ErrorCode { get; }
    public string SapMessage { get; }
    public string UserMessage { get; }
    public bool Retryable { get; }

    public SapIntegrationException(
        string errorCode,
        string sapMessage,
        string userMessage,
        bool retryable,
        Exception? inner = null)
        : base($"{errorCode}: {userMessage}", inner)
    {
        ErrorCode = errorCode;
        SapMessage = sapMessage;
        UserMessage = userMessage;
        Retryable = retryable;
    }
}