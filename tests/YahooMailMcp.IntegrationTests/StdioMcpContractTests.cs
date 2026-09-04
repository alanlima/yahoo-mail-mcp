using ModelContextProtocol.Client;

namespace YahooMailMcp.IntegrationTests;

public sealed class StdioMcpContractTests
{
    [Fact]
    public async Task OfficialClientInitializesAndListsExactToolSurface()
    {
        var serverAssembly = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "YahooMailMcp.Server.Stdio",
            "bin",
            "Release",
            "net10.0",
            "YahooMailMcp.Server.Stdio.dll"));
        Assert.True(File.Exists(serverAssembly), $"Stdio server was not built at {serverAssembly}.");

        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["Yahoo__Email"] = "configured-test-address";
        environment["Yahoo__AppPassword"] = "configured-test-app-password";
        environment["Cursor__SigningKey"] = "0123456789abcdef0123456789abcdef";
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "yahoo-mail-mcp-contract-test",
            Command = "dotnet",
            Arguments = [serverAssembly],
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).Order().ToArray();

        Assert.Equal(
            [
                "yahoo_mail_get_message",
                "yahoo_mail_list_folders",
                "yahoo_mail_list_messages",
                "yahoo_mail_mark_read",
                "yahoo_mail_mark_unread",
                "yahoo_mail_move_message",
                "yahoo_mail_search_messages",
                "yahoo_mail_set_flagged",
                "yahoo_mail_status"
            ],
            names);

        var result = await client.CallToolAsync(
            "yahoo_mail_list_messages",
            new Dictionary<string, object?> { ["folder"] = "INBOX", ["limit"] = 0 });

        Assert.NotNull(result.StructuredContent);
        Assert.Contains("\"success\":false", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_request", result.StructuredContent.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}