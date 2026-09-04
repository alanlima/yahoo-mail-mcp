using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

using MimeKit;

using YahooMailMcp.Application;
using YahooMailMcp.Domain;

using DomainFolderAttributes = YahooMailMcp.Domain.MailFolderAttributes;

namespace YahooMailMcp.Infrastructure.MailKit;

public sealed class YahooMailGateway(
    YahooImapSession session,
    ICursorCodec cursorCodec,
    MailLimits limits,
    TimeProvider timeProvider,
    DestinationFolderPolicy destinationFolderPolicy) : IYahooMailGateway
{
    private static readonly string[] AllowedHeaders =
        ["Message-Id", "In-Reply-To", "References", "Reply-To", "List-Id", "List-Unsubscribe"];

    private readonly MailLimits limits = limits.Validate();

    public Task<MailAccountStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        session.ExecuteReadAsync(
            "status",
            (client, features, _) => Task.FromResult(new MailAccountStatus(
                client.IsConnected,
                client.IsAuthenticated,
                features)),
            cancellationToken);

    public Task<IReadOnlyList<MailFolderInfo>> ListFoldersAsync(CancellationToken cancellationToken) =>
        session.ExecuteReadAsync<IReadOnlyList<MailFolderInfo>>("list_folders", async (client, _, token) =>
        {
            var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity;
            var folders = new List<IMailFolder>();
            foreach (var folderNamespace in client.PersonalNamespaces)
            {
                folders.AddRange(await client.GetFoldersAsync(
                    folderNamespace,
                    statusItems,
                    subscribedOnly: false,
                    token).ConfigureAwait(false));
            }

            if (folders.All(folder => !folder.FullName.Equals(client.Inbox.FullName, StringComparison.OrdinalIgnoreCase)))
            {
                await client.Inbox.StatusAsync(statusItems, token).ConfigureAwait(false);
                folders.Add(client.Inbox);
            }

            return folders
                .DistinctBy(folder => folder.FullName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(folder => folder.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(MapFolder)
                .ToArray();
        }, cancellationToken);

    public Task<PagedResult<MailMessageSummary>> ListMessagesAsync(
        ListMessagesRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request, limits);
        return FetchPageAsync("list_messages", request.Folder, request.Limit, request.Cursor, SearchQuery.All, null, cancellationToken);
    }

    public Task<PagedResult<MailMessageSummary>> SearchMessagesAsync(
        SearchMessagesRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request, limits);
        return FetchPageAsync(
            "search_messages",
            request.Folder,
            request.Limit,
            request.Cursor,
            BuildSearchQuery(request),
            request.HasAttachments,
            cancellationToken);
    }

    public Task<MailMessageDetail> GetMessageAsync(
        GetMessageRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request);
        return session.ExecuteReadAsync("get_message", async (client, _, token) =>
        {
            var folder = await GetFolderAsync(client, request.Id.Folder, token).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, token).ConfigureAwait(false);
            ValidateUidValidity(request.Id, folder.UidValidity);

            var uid = new UniqueId(request.Id.Uid);
            var fetchRequest = new FetchRequest(
                MessageSummaryItems.UniqueId
                | MessageSummaryItems.Envelope
                | MessageSummaryItems.InternalDate
                | MessageSummaryItems.Size
                | MessageSummaryItems.Flags
                | MessageSummaryItems.BodyStructure);
            var fetched = await folder.FetchAsync([uid], fetchRequest, token).ConfigureAwait(false);
            var providerSummary = fetched.SingleOrDefault()
                ?? throw new MailGatewayException(
                    MailErrorCodes.MessageNotFound,
                    "The requested message was not found.",
                    retryable: false);

            var summary = MapSummary(folder, providerSummary);
            var headers = request.BodyMode == MessageBodyMode.HeadersAndText
                ? await GetSafeHeadersAsync(folder, uid, token).ConfigureAwait(false)
                : new Dictionary<string, string>();
            var bodyText = request.BodyMode == MessageBodyMode.Metadata
                ? null
                : await GetBodyTextAsync(folder, uid, providerSummary, token).ConfigureAwait(false);
            var normalized = MailBodyText.NormalizeAndCap(bodyText, limits.MaxBodyCharacters);

            return new MailMessageDetail(summary, headers, normalized.Text, normalized.Truncated);
        }, cancellationToken);
    }

    public Task<MessageStateResult> SetReadStateAsync(
        SetReadStateRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request);
        return session.ExecuteMutationAsync("set_read_state", async (client, _, token) =>
        {
            var folder = await GetFolderAsync(client, request.Id.Folder, token).ConfigureAwait(false);
            await OpenWritableAsync(folder, token).ConfigureAwait(false);
            ValidateUidValidity(request.Id, folder.UidValidity);
            var uid = new UniqueId(request.Id.Uid);
            await EnsureMessageExistsAsync(folder, uid, token).ConfigureAwait(false);

            if (request.IsRead)
            {
                await folder.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, token).ConfigureAwait(false);
            }
            else
            {
                await folder.RemoveFlagsAsync(uid, MessageFlags.Seen, silent: true, token).ConfigureAwait(false);
            }

            return new MessageStateResult(
                new MailMessageId(folder.FullName, uid.Id, folder.UidValidity),
                request.IsRead,
                null);
        }, cancellationToken);
    }

    public Task<MessageStateResult> SetFlaggedStateAsync(
        SetFlaggedStateRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request);
        return session.ExecuteMutationAsync("set_flagged_state", async (client, _, token) =>
        {
            var folder = await GetFolderAsync(client, request.Id.Folder, token).ConfigureAwait(false);
            await OpenWritableAsync(folder, token).ConfigureAwait(false);
            ValidateUidValidity(request.Id, folder.UidValidity);
            var uid = new UniqueId(request.Id.Uid);
            await EnsureMessageExistsAsync(folder, uid, token).ConfigureAwait(false);

            if (request.IsFlagged)
            {
                await folder.AddFlagsAsync(uid, MessageFlags.Flagged, silent: true, token).ConfigureAwait(false);
            }
            else
            {
                await folder.RemoveFlagsAsync(uid, MessageFlags.Flagged, silent: true, token).ConfigureAwait(false);
            }

            return new MessageStateResult(
                new MailMessageId(folder.FullName, uid.Id, folder.UidValidity),
                null,
                request.IsFlagged);
        }, cancellationToken);
    }

    public Task<MoveMessageResult> MoveMessageAsync(
        MoveMessageRequest request,
        CancellationToken cancellationToken)
    {
        MailRequestValidator.Validate(request);
        return session.ExecuteMutationAsync("move_message", async (client, features, token) =>
        {
            var destination = await GetFolderAsync(client, request.DestinationFolder, token).ConfigureAwait(false);
            destinationFolderPolicy.Validate(request.SourceId, MapFolder(destination));
            MoveCapabilityPolicy.RequireNativeMove(features);

            var source = await GetFolderAsync(client, request.SourceId.Folder, token).ConfigureAwait(false);
            await OpenWritableAsync(source, token).ConfigureAwait(false);
            ValidateUidValidity(request.SourceId, source.UidValidity);
            var sourceUid = new UniqueId(request.SourceId.Uid);
            await EnsureMessageExistsAsync(source, sourceUid, token).ConfigureAwait(false);
            var destinationUid = await source.MoveToAsync(sourceUid, destination, token).ConfigureAwait(false);
            var sourceId = new MailMessageId(source.FullName, sourceUid.Id, source.UidValidity);
            var destinationId = destinationUid is null
                ? null
                : new MailMessageId(
                    destination.FullName,
                    destinationUid.Value.Id,
                    destination.UidValidity == 0 ? null : destination.UidValidity);

            return new MoveMessageResult(sourceId, destinationId, Completed: true);
        }, cancellationToken);
    }

    private Task<PagedResult<MailMessageSummary>> FetchPageAsync(
        string operationName,
        string folderName,
        int limit,
        string? encodedCursor,
        SearchQuery query,
        bool? hasAttachments,
        CancellationToken cancellationToken) =>
        session.ExecuteReadAsync(operationName, async (client, _, token) =>
        {
            var folder = await GetFolderAsync(client, folderName, token).ConfigureAwait(false);
            await folder.OpenAsync(FolderAccess.ReadOnly, token).ConfigureAwait(false);
            var cursor = DecodeCursor(encodedCursor, folder);
            var matchingUids = await folder.SearchAsync(query, token).ConfigureAwait(false);
            var candidates = matchingUids
                .Where(uid => cursor is null || uid.Id < cursor.LastUid)
                .OrderByDescending(uid => uid.Id)
                .Take(CandidateLimit(limit, hasAttachments))
                .ToArray();

            if (candidates.Length == 0)
            {
                return new PagedResult<MailMessageSummary>([], null);
            }

            var fetchRequest = new FetchRequest(
                MessageSummaryItems.UniqueId
                | MessageSummaryItems.Envelope
                | MessageSummaryItems.InternalDate
                | MessageSummaryItems.Size
                | MessageSummaryItems.Flags
                | MessageSummaryItems.BodyStructure);
            var fetched = await folder.FetchAsync(candidates, fetchRequest, token).ConfigureAwait(false);
            var filtered = fetched
                .Select(providerSummary => MapSummary(folder, providerSummary))
                .Where(summary => hasAttachments is null || summary.HasAttachments == hasAttachments)
                .OrderByDescending(summary => summary.ReceivedAtUtc)
                .ThenByDescending(summary => summary.Id.Uid)
                .ToArray();
            var page = filtered.Take(limit).ToArray();
            var hasMore = filtered.Length > limit || candidates.Length == CandidateLimit(limit, hasAttachments);
            var nextCursor = hasMore && page.Length > 0
                ? cursorCodec.Encode(new MailCursor(
                    1,
                    folder.FullName,
                    folder.UidValidity,
                    "descending",
                    candidates.Min(uid => uid.Id),
                    timeProvider.GetUtcNow().ToUnixTimeSeconds()))
                : null;

            return new PagedResult<MailMessageSummary>(page, nextCursor);
        }, cancellationToken);

    private static int CandidateLimit(int limit, bool? hasAttachments) =>
        hasAttachments is null ? limit + 1 : Math.Min((limit + 1) * 5, 250);

    private MailCursor? DecodeCursor(string? encodedCursor, IMailFolder folder)
    {
        if (string.IsNullOrWhiteSpace(encodedCursor))
        {
            return null;
        }

        var cursor = cursorCodec.Decode(encodedCursor);
        if (!cursor.Folder.Equals(folder.FullName, StringComparison.Ordinal)
            || !cursor.Direction.Equals("descending", StringComparison.Ordinal))
        {
            throw new MailGatewayException(
                MailErrorCodes.InvalidCursor,
                "The pagination cursor does not match this request.",
                retryable: false);
        }

        if (cursor.UidValidity is not null && cursor.UidValidity != folder.UidValidity)
        {
            throw new MailGatewayException(
                MailErrorCodes.UidValidityChanged,
                "The folder identity changed; restart pagination.",
                retryable: false);
        }

        return cursor;
    }

    private static SearchQuery BuildSearchQuery(SearchMessagesRequest request)
    {
        var query = SearchQuery.All;
        query = Add(query, request.Text, SearchQuery.MessageContains);
        query = Add(query, request.From, SearchQuery.FromContains);
        query = Add(query, request.To, SearchQuery.ToContains);
        query = Add(query, request.Subject, SearchQuery.SubjectContains);

        if (request.SinceUtc is not null)
        {
            query = query.And(SearchQuery.DeliveredAfter(request.SinceUtc.Value.UtcDateTime));
        }

        if (request.BeforeUtc is not null)
        {
            query = query.And(SearchQuery.DeliveredBefore(request.BeforeUtc.Value.UtcDateTime));
        }

        if (request.UnreadOnly)
        {
            query = query.And(SearchQuery.NotSeen);
        }

        if (request.FlaggedOnly)
        {
            query = query.And(SearchQuery.Flagged);
        }

        return query;
    }

    private static SearchQuery Add(SearchQuery query, string? value, Func<string, SearchQuery> create) =>
        string.IsNullOrWhiteSpace(value) ? query : query.And(create(value.Trim()));

    private static async Task<IMailFolder> GetFolderAsync(
        ImapClient client,
        string folderName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetFolderAsync(folderName, cancellationToken).ConfigureAwait(false);
        }
        catch (FolderNotFoundException exception)
        {
            throw new MailGatewayException(
                MailErrorCodes.FolderNotFound,
                "The requested mail folder was not found.",
                retryable: false,
                exception);
        }
    }

    private static async Task OpenWritableAsync(IMailFolder folder, CancellationToken cancellationToken)
    {
        var access = await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);
        if (access != FolderAccess.ReadWrite)
        {
            throw new MailGatewayException(
                MailErrorCodes.FolderReadOnly,
                "The requested mail folder is read-only.",
                retryable: false);
        }
    }

    private static async Task EnsureMessageExistsAsync(
        IMailFolder folder,
        UniqueId uid,
        CancellationToken cancellationToken)
    {
        var fetched = await folder.FetchAsync(
            [uid],
            new FetchRequest(MessageSummaryItems.UniqueId),
            cancellationToken).ConfigureAwait(false);
        if (fetched.Count == 0)
        {
            throw new MailGatewayException(
                MailErrorCodes.MessageNotFound,
                "The requested message was not found.",
                retryable: false);
        }
    }

    private static void ValidateUidValidity(MailMessageId id, uint currentUidValidity)
    {
        if (id.UidValidity is not null && id.UidValidity != currentUidValidity)
        {
            throw new MailGatewayException(
                MailErrorCodes.UidValidityChanged,
                "The folder identity changed; locate the message again.",
                retryable: false);
        }
    }

    private static MailFolderInfo MapFolder(IMailFolder folder) =>
        new(
            folder.FullName,
            folder.Name,
            folder.DirectorySeparator,
            MapFolderAttributes(folder.Attributes),
            folder.Unread,
            folder.Count,
            folder.Attributes.HasFlag(FolderAttributes.Subscribed));

    private static DomainFolderAttributes MapFolderAttributes(FolderAttributes attributes)
    {
        var result = DomainFolderAttributes.None;
        result |= attributes.HasFlag(FolderAttributes.Inbox) ? DomainFolderAttributes.Inbox : 0;
        result |= attributes.HasFlag(FolderAttributes.Drafts) ? DomainFolderAttributes.Drafts : 0;
        result |= attributes.HasFlag(FolderAttributes.Sent) ? DomainFolderAttributes.Sent : 0;
        result |= attributes.HasFlag(FolderAttributes.Junk) ? DomainFolderAttributes.Junk : 0;
        result |= attributes.HasFlag(FolderAttributes.Trash) ? DomainFolderAttributes.Trash : 0;
        result |= attributes.HasFlag(FolderAttributes.Archive) ? DomainFolderAttributes.Archive : 0;
        result |= attributes.HasFlag(FolderAttributes.NoSelect) ? DomainFolderAttributes.NoSelect : 0;
        result |= attributes.HasFlag(FolderAttributes.HasChildren) ? DomainFolderAttributes.HasChildren : 0;
        result |= attributes.HasFlag(FolderAttributes.HasNoChildren) ? DomainFolderAttributes.HasNoChildren : 0;
        return result;
    }

    private static MailMessageSummary MapSummary(IMailFolder folder, IMessageSummary summary)
    {
        var attachments = summary.Attachments.OfType<BodyPartBasic>().Select(MapAttachment).ToArray();
        var flags = MailMessageState.None;
        flags |= summary.Flags?.HasFlag(MessageFlags.Seen) == true ? MailMessageState.Seen : 0;
        flags |= summary.Flags?.HasFlag(MessageFlags.Answered) == true ? MailMessageState.Answered : 0;
        flags |= summary.Flags?.HasFlag(MessageFlags.Flagged) == true ? MailMessageState.Flagged : 0;
        flags |= summary.Flags?.HasFlag(MessageFlags.Draft) == true ? MailMessageState.Draft : 0;
        flags |= summary.Flags?.HasFlag(MessageFlags.Recent) == true ? MailMessageState.Recent : 0;

        return new MailMessageSummary(
            new MailMessageId(folder.FullName, summary.UniqueId.Id, folder.UidValidity),
            summary.Envelope?.Subject,
            MapAddresses(summary.Envelope?.From),
            MapAddresses(summary.Envelope?.To),
            summary.Envelope?.Date?.ToUniversalTime(),
            summary.InternalDate?.ToUniversalTime(),
            summary.Size,
            flags,
            attachments.Length > 0,
            attachments);
    }

    private static MailAttachmentInfo MapAttachment(BodyPartBasic attachment) =>
        new(
            attachment.FileName,
            attachment.ContentType.MimeType,
            attachment.Octets,
            attachment.ContentId);

    private static MailAddress[] MapAddresses(InternetAddressList? addresses) =>
        addresses?.Mailboxes
            .Select(mailbox => new MailAddress(mailbox.Name, mailbox.Address))
            .ToArray()
        ?? [];

    private static async Task<IReadOnlyDictionary<string, string>> GetSafeHeadersAsync(
        IMailFolder folder,
        UniqueId uid,
        CancellationToken cancellationToken)
    {
        var headers = await folder.GetHeadersAsync(uid, cancellationToken).ConfigureAwait(false);
        return AllowedHeaders
            .Select(name => (Name: name, Value: headers[name]))
            .Where(header => !string.IsNullOrWhiteSpace(header.Value))
            .ToDictionary(header => header.Name, header => header.Value!, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string?> GetBodyTextAsync(
        IMailFolder folder,
        UniqueId uid,
        IMessageSummary summary,
        CancellationToken cancellationToken)
    {
        var bodyPart = summary.TextBody ?? summary.HtmlBody;
        if (bodyPart is null)
        {
            return null;
        }

        var entity = await folder.GetBodyPartAsync(uid, bodyPart, cancellationToken).ConfigureAwait(false);
        if (entity is not TextPart textPart)
        {
            return null;
        }

        return textPart.IsHtml ? MailBodyText.FromHtml(textPart.Text) : textPart.Text;
    }
}