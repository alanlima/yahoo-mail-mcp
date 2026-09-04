namespace YahooMailMcp.Domain;

public static class MailErrorCodes
{
    public const string AuthenticationFailed = "authentication_failed";
    public const string ConnectionFailed = "connection_failed";
    public const string ConnectionTimeout = "connection_timeout";
    public const string FolderNotFound = "folder_not_found";
    public const string FolderReadOnly = "folder_read_only";
    public const string MessageNotFound = "message_not_found";
    public const string UidValidityChanged = "uid_validity_changed";
    public const string InvalidCursor = "invalid_cursor";
    public const string InvalidRequest = "invalid_request";
    public const string ResultLimitExceeded = "result_limit_exceeded";
    public const string TrashDestinationForbidden = "trash_destination_forbidden";
    public const string OperationNotSupported = "operation_not_supported";
    public const string ProviderRateLimited = "provider_rate_limited";
    public const string ProviderError = "provider_error";
    public const string OperationCancelled = "operation_cancelled";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";
}

public sealed class MailGatewayException : Exception
{
    public MailGatewayException(string code, string safeMessage, bool retryable, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }
}