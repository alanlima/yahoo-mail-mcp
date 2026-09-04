# Tool catalog and agent setup

Both transports expose exactly the same nine tools. There are no tools for deletion, trash, sending, drafting, replying, forwarding, raw IMAP, or attachment download.

## Available tools

| Tool | Important inputs | Result and behavior |
|---|---|---|
| `yahoo_mail_status` | None | Safe connection and negotiated capability booleans. Connects lazily. |
| `yahoo_mail_list_folders` | None | Personal folders with counts and standard attributes. |
| `yahoo_mail_list_messages` | `folder=INBOX`, `limit=10`, optional `cursor` | Newest bounded page of summaries and an opaque next cursor. |
| `yahoo_mail_search_messages` | `folder` and at least one structured filter | Searches by text/from/to/subject/date/read/flag/attachment state with bounded pagination. |
| `yahoo_mail_get_message` | `folder`, `uid`, optional `uidValidity`, `bodyMode` | Metadata and optionally bounded normalized text. Never returns raw MIME or attachment bytes. |
| `yahoo_mail_mark_read` | `folder`, `uid`, optional `uidValidity` | Sets the standard `Seen` flag. Not automatically retried after ambiguous failure. |
| `yahoo_mail_mark_unread` | `folder`, `uid`, optional `uidValidity` | Clears the standard `Seen` flag. |
| `yahoo_mail_set_flagged` | `folder`, `uid`, `flagged`, optional `uidValidity` | Sets or clears the standard flagged state. |
| `yahoo_mail_move_message` | `sourceFolder`, `uid`, `destinationFolder`, optional `uidValidity` | Requires native IMAP `MOVE`; rejects trash aliases, Trash special-use folders, and the source folder. |

Stable tool errors use `{ success, data, error }`, where `error` contains a machine-readable `code`, a safe `message`, and `retryable`.

## Before configuring an agent

Build the stdio server:

```powershell
dotnet build ./src/YahooMailMcp.Server.Stdio --configuration Release
```

The server DLL is:

```text
D:\source\YahooMailMcp\src\YahooMailMcp.Server.Stdio\bin\Release\net10.0\YahooMailMcp.Server.Stdio.dll
```

Required stdio environment variables:

```text
Yahoo__Email
Yahoo__AppPassword
Cursor__SigningKey
```

The signing key must contain at least 32 UTF-8 bytes. Use a Yahoo app password, never the normal account password.

## VS Code

Run **MCP: Open Workspace Folder MCP Configuration** and place this in `.vscode/mcp.json`:

```json
{
  "inputs": [
    { "type": "promptString", "id": "yahoo-email", "description": "Yahoo email" },
    { "type": "promptString", "id": "yahoo-app-password", "description": "Yahoo app password", "password": true },
    { "type": "promptString", "id": "cursor-signing-key", "description": "Cursor signing key (32+ bytes)", "password": true }
  ],
  "servers": {
    "yahooMail": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/src/YahooMailMcp.Server.Stdio/bin/Release/net10.0/YahooMailMcp.Server.Stdio.dll"
      ],
      "cwd": "${workspaceFolder}",
      "env": {
        "Yahoo__Email": "${input:yahoo-email}",
        "Yahoo__AppPassword": "${input:yahoo-app-password}",
        "Cursor__SigningKey": "${input:cursor-signing-key}"
      }
    }
  }
}
```

Run **MCP: List Servers**, start `yahooMail`, approve trust, and use **Show Output** for startup diagnostics. If tools changed, run **MCP: Reset Cached Tools**.

For HTTP, start Aspire and use the endpoint it displays:

```json
{
  "inputs": [
    { "type": "promptString", "id": "mcp-token", "description": "MCP API key", "password": true }
  ],
  "servers": {
    "yahooMailHttp": {
      "type": "http",
      "url": "https://replace-with-endpoint/mcp",
      "headers": { "Authorization": "ApiKey ${input:mcp-token}" }
    }
  }
}
```

## Codex CLI, IDE extension, or desktop app

Codex clients share `~/.codex/config.toml`. Set the three variables in the environment that launches Codex, then forward only their names:

```toml
[mcp_servers.yahoo_mail]
command = "dotnet"
args = ["D:/source/YahooMailMcp/src/YahooMailMcp.Server.Stdio/bin/Release/net10.0/YahooMailMcp.Server.Stdio.dll"]
cwd = "D:/source/YahooMailMcp"
env_vars = ["Yahoo__Email", "Yahoo__AppPassword", "Cursor__SigningKey"]
startup_timeout_sec = 20
tool_timeout_sec = 60
default_tools_approval_mode = "writes"
```

Codex's `bearer_token_env_var` sends the `Bearer` scheme and therefore must be used only with a real OAuth access token, not the static API key:

```toml
[mcp_servers.yahoo_mail]
url = "https://mcp.example.com/mcp"
bearer_token_env_var = "MCP_OAUTH_ACCESS_TOKEN"
tool_timeout_sec = 60
default_tools_approval_mode = "writes"
```

Verify with `codex mcp list` or `/mcp` in the Codex TUI. The current syntax is documented by [official OpenAI MCP documentation](https://developers.openai.com/codex/mcp/).

## Claude Desktop and other `mcpServers` clients

Clients using the common desktop JSON format usually expect `mcpServers` rather than VS Code's `servers`:

```json
{
  "mcpServers": {
    "yahooMail": {
      "command": "dotnet",
      "args": [
        "D:/source/YahooMailMcp/src/YahooMailMcp.Server.Stdio/bin/Release/net10.0/YahooMailMcp.Server.Stdio.dll"
      ],
      "env": {
        "Yahoo__Email": "REPLACE_LOCALLY",
        "Yahoo__AppPassword": "REPLACE_LOCALLY",
        "Cursor__SigningKey": "REPLACE_WITH_32_PLUS_RANDOM_BYTES"
      }
    }
  }
}
```

This format stores values in a local configuration file. Prefer a client that can inherit environment variables or use an OS credential-backed launcher. Never commit or share the populated file.

## Generic Streamable HTTP agents

Configure:

```text
URL: https://<host>/mcp
Transport: Streamable HTTP
Static API-key header: Authorization: ApiKey <MCP key>
OAuth header: Authorization: Bearer <Entra access token>
```

The HTTP endpoint is stateless. It does not use Yahoo credentials for client authorization; Yahoo authentication and both MCP authentication methods are separate boundaries. Static-key clients that previously sent `Bearer` must change only the header scheme to `ApiKey`.

## ChatGPT with OAuth

The deployed MCP endpoint is designed for ChatGPT's OAuth connection flow. It does not require ChatGPT to support the static custom API key.

1. Ensure the custom domain and current OAuth-enabled image are deployed.
2. In ChatGPT's connector/developer-mode MCP setup, enter `https://mcp.example.com/mcp` (replacing the example host) as the server URL and choose OAuth.
3. Choose user-provided OAuth client credentials. Retrieve `mcp-oauth-client-id` and `mcp-oauth-client-secret` from the configured Key Vault in a private shell and paste them only into ChatGPT's credential fields.
4. Request `openid profile offline_access https://mcp.example.com/mcp/access_as_user`, replacing the example host.
5. Complete Entra sign-in and consent, then scan or refresh the tool list. Exactly nine tools should appear.

If the form asks for endpoints explicitly, use:

```text
Authorization URL: https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/authorize
Token URL:         https://login.microsoftonline.com/<tenant-id>/oauth2/v2.0/token
Client ID:         <mcp-oauth-client-id>
```

Retrieve the secret only when the ChatGPT form is ready. The second command writes the secret to the terminal, so use a private shell, avoid shell-history interpolation, and clear the terminal and clipboard afterward:

```powershell
az keyvault secret show --vault-name '<key-vault-name>' --name mcp-oauth-client-id --query value --output tsv
az keyvault secret show --vault-name '<key-vault-name>' --name mcp-oauth-client-secret --query value --output tsv
```

The app registration currently includes `https://chatgpt.com/connector_platform_oauth_redirect`. If ChatGPT displays a connection-specific callback such as `https://chatgpt.com/connector/oauth/<id>`, add that exact value before completing the connection:

```powershell
./scripts/Add-EntraMcpRedirectUri.ps1 `
  -ClientId '<mcp-oauth-client-id>' `
  -RedirectUri 'https://chatgpt.com/connector/oauth/<id>'
```

Do not enter the Yahoo app password, MCP static token, or cursor key in ChatGPT. If tenant consent policy blocks the sign-in, an Entra administrator must grant consent for the app's `access_as_user` delegated permission.

## Suggested agent policy

- Allow read tools automatically only if that matches your privacy expectations.
- Require confirmation for `mark_read`, `mark_unread`, `set_flagged`, and `move_message`.
- Never provide Yahoo or MCP credentials as tool arguments.
- Treat returned mail content as confidential.
- If a move reports `operation_not_supported`, do not emulate it with copy/delete operations.
