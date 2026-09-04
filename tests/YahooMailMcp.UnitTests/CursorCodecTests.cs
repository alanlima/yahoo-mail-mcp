using YahooMailMcp.Application;
using YahooMailMcp.Domain;

namespace YahooMailMcp.UnitTests;

public sealed class CursorCodecTests
{
    private const string SigningKey = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTripPreservesVersionedPosition()
    {
        var codec = CreateCodec();
        var expected = new MailCursor(1, "INBOX", 17, "descending", 42, Now.ToUnixTimeSeconds());

        var decoded = codec.Decode(codec.Encode(expected));

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void ChangedPayloadIsRejected()
    {
        var codec = CreateCodec();
        var encoded = codec.Encode(new MailCursor(1, "INBOX", 17, "descending", 42, Now.ToUnixTimeSeconds()));
        var replacement = encoded[0] == 'A' ? 'B' : 'A';
        var tampered = replacement + encoded[1..];

        var exception = Assert.Throws<MailGatewayException>(() => codec.Decode(tampered));

        Assert.Equal(MailErrorCodes.InvalidCursor, exception.Code);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public void ExpiredCursorIsRejected()
    {
        var codec = CreateCodec(TimeSpan.FromMinutes(30));
        var encoded = codec.Encode(new MailCursor(
            1,
            "INBOX",
            17,
            "descending",
            42,
            Now.AddHours(-1).ToUnixTimeSeconds()));

        var exception = Assert.Throws<MailGatewayException>(() => codec.Decode(encoded));

        Assert.Equal(MailErrorCodes.InvalidCursor, exception.Code);
    }

    [Fact]
    public void UnsupportedVersionCannotBeEncoded()
    {
        var codec = CreateCodec();
        var cursor = new MailCursor(2, "INBOX", 17, "descending", 42, Now.ToUnixTimeSeconds());

        var exception = Assert.Throws<MailGatewayException>(() => codec.Encode(cursor));

        Assert.Equal(MailErrorCodes.InvalidCursor, exception.Code);
    }

    private static CursorCodec CreateCodec(TimeSpan? maximumAge = null) =>
        new(SigningKey, maximumAge, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}