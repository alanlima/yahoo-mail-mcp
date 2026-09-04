using System.Reflection;

using Microsoft.Extensions.Hosting;

using ModelContextProtocol.Server;

using NetArchTest.Rules;

using YahooMailMcp.Application;
using YahooMailMcp.Domain;
using YahooMailMcp.Infrastructure.MailKit;
using YahooMailMcp.Mcp;

namespace YahooMailMcp.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void DomainHasNoOutwardProjectOrFrameworkDependencies()
    {
        var referencedAssemblies = typeof(MailMessageId).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referencedAssemblies, reference =>
            reference.Name is not null
            && (reference.Name.StartsWith("YahooMailMcp.", StringComparison.Ordinal)
                || reference.Name.StartsWith("MailKit", StringComparison.Ordinal)
                || reference.Name.StartsWith("ModelContextProtocol", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || reference.Name.StartsWith("Azure.", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplicationDoesNotDependOnInfrastructureOrServers()
    {
        var result = Types.InAssembly(typeof(ApplicationAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "YahooMailMcp.Infrastructure.MailKit",
                "YahooMailMcp.Server.Http",
                "YahooMailMcp.Server.Stdio",
                "MailKit",
                "ModelContextProtocol",
                "Microsoft.AspNetCore",
                "Azure")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result.FailingTypeNames));
    }

    [Fact]
    public void GatewayContractContainsOnlyApprovedOperationsThroughPhaseTwo()
    {
        var methodNames = typeof(IYahooMailGateway).GetMethods().Select(method => method.Name).Order().ToArray();

        Assert.Equal(
            [
                "GetMessageAsync",
                "GetStatusAsync",
                "ListFoldersAsync",
                "ListMessagesAsync",
                "MoveMessageAsync",
                "SearchMessagesAsync",
                "SetFlaggedStateAsync",
                "SetReadStateAsync"
            ],
            methodNames);
    }

    [Fact]
    public void ProductionAssembliesExposeNoForbiddenActionMethods()
    {
        string[] forbiddenTerms = ["delete", "expunge", "purge"];
        var assemblies = new[]
        {
            typeof(MailMessageId).Assembly,
            typeof(ApplicationAssemblyMarker).Assembly,
            typeof(YahooMailGateway).Assembly,
            typeof(YahooMailTools).Assembly,
            Assembly.Load("YahooMailMcp.Server.Http"),
            Assembly.Load("YahooMailMcp.Server.Stdio")
        };

        var violations = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => !method.IsSpecialName)
            .Where(method => forbiddenTerms.Any(term => method.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .Distinct()
            .Order()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void McpAdapterDependsOnApplicationRatherThanMailKit()
    {
        var result = Types.InAssembly(typeof(YahooMailTools).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("MailKit", "YahooMailMcp.Infrastructure.MailKit")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result.FailingTypeNames));
    }

    [Fact]
    public void McpToolSurfaceContainsOnlyApprovedOperations()
    {
        var toolNames = typeof(YahooMailTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Name!)
            .Order()
            .ToArray();

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
            toolNames);

        string[] forbiddenTerms = ["delete", "remove", "trash", "purge", "cleanup", "expunge"];
        Assert.DoesNotContain(toolNames, name =>
            forbiddenTerms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void InfrastructureRegistrationDependsOnlyOnHostBuilder()
    {
        var method = typeof(HostApplicationBuilderExtensions).GetMethod(
            "AddYahooMailInfrastructure",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(IHostApplicationBuilder), parameter.ParameterType);
    }

    private static string FormatFailures(IEnumerable<string>? failures) =>
        failures is null ? "Architecture rule failed." : string.Join(Environment.NewLine, failures);
}