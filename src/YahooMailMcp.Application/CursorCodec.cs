using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public sealed record MailCursor(
    int Version,
    string Folder,
    uint? UidValidity,
    string Direction,
    uint LastUid,
    long IssuedAtUnixSeconds);

public interface ICursorCodec
{
    string Encode(MailCursor cursor);

    MailCursor Decode(string encodedCursor);
}

public sealed class CursorCodec : ICursorCodec
{
    private const int CurrentVersion = 1;
    private readonly byte[] signingKey;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan maximumAge;

    public CursorCodec(string signingKey, TimeSpan? maximumAge = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);
        this.signingKey = Encoding.UTF8.GetBytes(signingKey);
        if (this.signingKey.Length < 32)
        {
            throw new ArgumentException("Cursor signing key must contain at least 32 UTF-8 bytes.", nameof(signingKey));
        }

        this.maximumAge = maximumAge ?? TimeSpan.FromHours(24);
        if (this.maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Encode(MailCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        if (cursor.Version != CurrentVersion)
        {
            throw InvalidCursor();
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(cursor);
        var signature = HMACSHA256.HashData(signingKey, payload);
        return $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
    }

    public MailCursor Decode(string encodedCursor)
    {
        try
        {
            var parts = encodedCursor.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw InvalidCursor();
            }

            var payload = Base64UrlDecode(parts[0]);
            var suppliedSignature = Base64UrlDecode(parts[1]);
            var expectedSignature = HMACSHA256.HashData(signingKey, payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw InvalidCursor();
            }

            var cursor = JsonSerializer.Deserialize<MailCursor>(payload) ?? throw InvalidCursor();
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(cursor.IssuedAtUnixSeconds);
            var now = timeProvider.GetUtcNow();
            if (cursor.Version != CurrentVersion || issuedAt > now || now - issuedAt > maximumAge)
            {
                throw InvalidCursor();
            }

            return cursor;
        }
        catch (MailGatewayException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw InvalidCursor(exception);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            0 => padded,
            2 => padded + "==",
            3 => padded + "=",
            _ => throw new FormatException("Invalid base64url value.")
        };
        return Convert.FromBase64String(padded);
    }

    private static MailGatewayException InvalidCursor(Exception? innerException = null) =>
        new(MailErrorCodes.InvalidCursor, "The pagination cursor is invalid or expired.", retryable: false, innerException);
}