using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.DependencyInjection;

namespace YahooMailMcp.AspireTests;

public sealed class AppHostModelTests
{
    [Fact]
    public async Task AppHostModelContainsHttpServer()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.YahooMailMcp_AppHost>(
            cancellationTokenSource.Token);

        await using var app = await builder.BuildAsync(cancellationTokenSource.Token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(model.Resources, resource =>
            resource.Name.Equals("yahoo-mail-mcp-http", StringComparison.Ordinal));

        Assert.Equal("yahoo-mail-mcp-http", resource.Name);
    }

    [Fact]
    public async Task LocalImapProfileUsesPinnedContainerAndRandomHostPorts()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.YahooMailMcp_AppHost>(
            ["--YahooMail:Profile=local-imap"],
            cancellationTokenSource.Token);

        await using var app = await builder.BuildAsync(cancellationTokenSource.Token);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var imap = Assert.Single(model.Resources, resource => resource.Name == "imap-test");
        var container = Assert.IsType<ContainerResource>(imap);
        var image = Assert.Single(container.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Contains("sha256:3df66b7", image.Tag, StringComparison.Ordinal);
        var endpoints = container.Annotations.OfType<EndpointAnnotation>().ToArray();

        Assert.Contains(endpoints, endpoint => endpoint.Name == "imap" && endpoint.TargetPort == 3143);
        Assert.Contains(endpoints, endpoint => endpoint.Name == "smtp" && endpoint.TargetPort == 3025);
        Assert.All(endpoints, endpoint => Assert.Null(endpoint.Port));
    }
}