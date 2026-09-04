using YahooMailMcp.Domain;

namespace YahooMailMcp.Application;

public interface IYahooMailGateway
{
    Task<MailAccountStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken);

    Task<PagedResult<MailMessageSummary>> ListMessagesAsync(
        ListMessagesRequest request,
        CancellationToken cancellationToken);

    Task<PagedResult<MailMessageSummary>> SearchMessagesAsync(
        SearchMessagesRequest request,
        CancellationToken cancellationToken);

    Task<MailMessageDetail> GetMessageAsync(
        GetMessageRequest request,
        CancellationToken cancellationToken);

    Task<MessageStateResult> SetReadStateAsync(
        SetReadStateRequest request,
        CancellationToken cancellationToken);

    Task<MessageStateResult> SetFlaggedStateAsync(
        SetFlaggedStateRequest request,
        CancellationToken cancellationToken);

    Task<MoveMessageResult> MoveMessageAsync(
        MoveMessageRequest request,
        CancellationToken cancellationToken);
}