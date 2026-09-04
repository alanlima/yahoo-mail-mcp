using YahooMailMcp.Infrastructure.MailKit;

namespace YahooMailMcp.UnitTests;

public sealed class YahooOptionsValidatorTests
{
    private readonly YahooOptionsValidator validator = new();

    [Fact]
    public void ProductionDefaultsWithCredentialsAreValid()
    {
        var options = ValidOptions();

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ProductionRejectsNonYahooEndpoint()
    {
        var options = ValidOptions();
        options.Host = "localhost";

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("imap.mail.yahoo.com", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitTestOverrideAllowsLocalEndpoint()
    {
        var options = ValidOptions();
        options.Host = "localhost";
        options.Port = 1143;
        options.UseSsl = false;
        options.AllowNonYahooEndpoint = true;

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MissingCredentialsAreRejectedWithoutIncludingValues()
    {
        var options = ValidOptions();
        options.Email = null;
        options.AppPassword = null;

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }

    private static YahooOptions ValidOptions() => new()
    {
        Email = "configured-address",
        AppPassword = "configured-app-password"
    };
}