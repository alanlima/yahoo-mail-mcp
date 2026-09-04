using System.ComponentModel;

using ModelContextProtocol.Server;

using YahooMailMcp.Application;
using YahooMailMcp.Domain;

namespace YahooMailMcp.Mcp;

[McpServerToolType]
public static class YahooMailTools
{
    public const string StatusToolName = "yahoo_mail_status";
    public const string ListFoldersToolName = "yahoo_mail_list_folders";
    public const string ListMessagesToolName = "yahoo_mail_list_messages";
    public const string SearchMessagesToolName = "yahoo_mail_search_messages";
    public const string GetMessageToolName = "yahoo_mail_get_message";
    public const string MarkReadToolName = "yahoo_mail_mark_read";
    public const string MarkUnreadToolName = "yahoo_mail_mark_unread";
    public const string SetFlaggedToolName = "yahoo_mail_set_flagged";
    public const string MoveMessageToolName = "yahoo_mail_move_message";

    [McpServerTool(Name = StatusToolName, UseStructuredContent = true)]
    [Description("Reports Yahoo IMAP connection state and safe capability booleans.")]
    public static Task<MailToolResult<MailAccountStatus>> GetStatusAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        CancellationToken cancellationToken) =>
        telemetry.InvokeAsync(StatusToolName, () => gateway.GetStatusAsync(cancellationToken));

    [McpServerTool(Name = ListFoldersToolName, UseStructuredContent = true)]
    [Description("Lists personal Yahoo Mail folders and available counts.")]
    public static Task<MailToolResult<IReadOnlyList<MailFolderInfo>>> ListFoldersAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        CancellationToken cancellationToken) =>
        telemetry.InvokeAsync(ListFoldersToolName, () => gateway.ListFoldersAsync(cancellationToken));

    [McpServerTool(Name = ListMessagesToolName, UseStructuredContent = true)]
    [Description("Lists a bounded page of recent message summaries from one folder.")]
    public static Task<MailToolResult<PagedResult<MailMessageSummary>>> ListMessagesAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        [Description("Folder full name.")] string folder = "INBOX",
        [Description("Maximum messages to return.")] int limit = 10,
        [Description("Opaque cursor from a previous page.")] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(
            ListMessagesToolName,
            () => gateway.ListMessagesAsync(new ListMessagesRequest(folder, limit, cursor), cancellationToken));

    [McpServerTool(Name = SearchMessagesToolName, UseStructuredContent = true)]
    [Description("Searches one folder using structured standard IMAP filters.")]
    public static Task<MailToolResult<PagedResult<MailMessageSummary>>> SearchMessagesAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        [Description("Folder full name.")] string folder,
        string? text = null,
        string? from = null,
        string? to = null,
        string? subject = null,
        DateTimeOffset? sinceUtc = null,
        DateTimeOffset? beforeUtc = null,
        bool unreadOnly = false,
        bool flaggedOnly = false,
        bool? hasAttachments = null,
        int limit = 10,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(SearchMessagesToolName, () => gateway.SearchMessagesAsync(
            new SearchMessagesRequest(
                folder,
                text,
                from,
                to,
                subject,
                sinceUtc,
                beforeUtc,
                unreadOnly,
                flaggedOnly,
                hasAttachments,
                limit,
                cursor),
            cancellationToken));

    [McpServerTool(Name = GetMessageToolName, UseStructuredContent = true)]
    [Description("Reads message metadata and optionally bounded normalized text from one folder UID.")]
    public static Task<MailToolResult<MailMessageDetail>> GetMessageAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        string folder,
        uint uid,
        uint? uidValidity = null,
        MessageBodyMode bodyMode = MessageBodyMode.Text,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(GetMessageToolName, () => gateway.GetMessageAsync(
            new GetMessageRequest(new MailMessageId(folder, uid, uidValidity), bodyMode),
            cancellationToken));

    [McpServerTool(Name = MarkReadToolName, UseStructuredContent = true)]
    [Description("Marks one Yahoo Mail message as read.")]
    public static Task<MailToolResult<MessageStateResult>> MarkReadAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        string folder,
        uint uid,
        uint? uidValidity = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(MarkReadToolName, () => gateway.SetReadStateAsync(
            new SetReadStateRequest(new MailMessageId(folder, uid, uidValidity), IsRead: true),
            cancellationToken));

    [McpServerTool(Name = MarkUnreadToolName, UseStructuredContent = true)]
    [Description("Marks one Yahoo Mail message as unread.")]
    public static Task<MailToolResult<MessageStateResult>> MarkUnreadAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        string folder,
        uint uid,
        uint? uidValidity = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(MarkUnreadToolName, () => gateway.SetReadStateAsync(
            new SetReadStateRequest(new MailMessageId(folder, uid, uidValidity), IsRead: false),
            cancellationToken));

    [McpServerTool(Name = SetFlaggedToolName, UseStructuredContent = true)]
    [Description("Sets or clears the standard flagged state for one Yahoo Mail message.")]
    public static Task<MailToolResult<MessageStateResult>> SetFlaggedAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        string folder,
        uint uid,
        bool flagged,
        uint? uidValidity = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(SetFlaggedToolName, () => gateway.SetFlaggedStateAsync(
            new SetFlaggedStateRequest(new MailMessageId(folder, uid, uidValidity), flagged),
            cancellationToken));

    [McpServerTool(Name = MoveMessageToolName, UseStructuredContent = true)]
    [Description("Moves one message to a validated organization folder when native IMAP MOVE is available.")]
    public static Task<MailToolResult<MoveMessageResult>> MoveMessageAsync(
        IYahooMailGateway gateway,
        McpToolTelemetry telemetry,
        string sourceFolder,
        uint uid,
        string destinationFolder,
        uint? uidValidity = null,
        CancellationToken cancellationToken = default) =>
        telemetry.InvokeAsync(MoveMessageToolName, () => gateway.MoveMessageAsync(
            new MoveMessageRequest(new MailMessageId(sourceFolder, uid, uidValidity), destinationFolder),
            cancellationToken));

}