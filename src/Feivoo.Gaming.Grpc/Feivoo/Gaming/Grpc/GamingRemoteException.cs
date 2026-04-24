namespace Feivoo.Gaming.Grpc;

/// <summary>
/// Thrown by Gaming handlers to signal a structured business failure.
/// Vertex's messaging layer ships <c>ex.Message</c> as the error response
/// payload, so the <see cref="Message"/> property is pre-encoded with the
/// error code prefix so client SDKs can recover the structured
/// <see cref="ResultErrorCode"/> on the other side of the wire.
/// </summary>
public sealed class GamingRemoteException : Exception
{
    /// <summary>Structured error code (transported via the encoded Message).</summary>
    public ResultErrorCode ErrorCode { get; }

    /// <summary>Original user-facing message without the code prefix.</summary>
    public string UserMessage { get; }

    public GamingRemoteException(ResultErrorCode code, string message)
        : base(Encode(code, message))
    {
        ErrorCode = code;
        UserMessage = message ?? string.Empty;
    }

    public GamingRemoteException(ResultErrorCode code, string message, Exception innerException)
        : base(Encode(code, message), innerException)
    {
        ErrorCode = code;
        UserMessage = message ?? string.Empty;
    }

    /// <summary>
    /// Wire-format separator between code name and user message. Client SDKs
    /// split on the first <see cref="Delimiter"/> to recover
    /// <see cref="ErrorCode"/> and <see cref="UserMessage"/>. Pipe (<c>|</c>)
    /// avoids false positives from messages containing colons, URLs, or
    /// IPv6 addresses.
    /// </summary>
    public const char Delimiter = '|';

    private static string Encode(ResultErrorCode code, string message)
        => $"{code}{Delimiter}{message ?? string.Empty}";
}
