using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using YahooMailMcp.Infrastructure.MailKit;

namespace YahooMailMcp.UnitTests;

public sealed class HostApplicationBuilderExtensionsTests
{
    [Fact]
    public void ConfigurationProviderAddedAfterRegistrationIsObserved()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddYahooMailInfrastructure();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Yahoo:Email"] = "late-bound-address",
            ["Yahoo:AppPassword"] = "late-bound-app-password",
            ["Cursor:SigningKey"] = "0123456789abcdef0123456789abcdef"
        });

        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<YahooOptions>>().Value;

        Assert.Equal("late-bound-address", options.Email);
        Assert.Equal("late-bound-app-password", options.AppPassword);
    }
}