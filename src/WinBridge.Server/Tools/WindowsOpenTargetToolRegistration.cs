// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.Tools;

internal static class WindowsOpenTargetToolRegistration
{
    public static McpServerTool Create(Func<WindowTools> getWindowTools)
    {
        ArgumentNullException.ThrowIfNull(getWindowTools);

        ValueTask<CallToolResult> Handler(
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken) =>
            new(getWindowTools().OpenTarget(requestContext, cancellationToken));

        McpServerTool tool = McpServerTool.Create(
            (Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)Handler,
            new McpServerToolCreateOptions
            {
                Name = ToolNames.WindowsOpenTarget,
                Description = ToolDescriptions.WindowsOpenTargetTool,
                ReadOnly = false,
                Destructive = false,
                Idempotent = false,
                OpenWorld = true,
                UseStructuredContent = true,
            });

        tool.ProtocolTool.InputSchema = CreateInputSchema();
        return tool;
    }

    private static JsonElement CreateInputSchema()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "targetKind": {
                  "type": "string",
                  "description": "Тип shell-open target. V1 поддерживает только document, folder и url. Поле обязательно и не заменяется эвристикой по строке target."
                },
                "target": {
                  "type": "string",
                  "description": "Сам target для shell-open. Для document и folder допустим только absolute local/UNC path. Для url допустим только absolute http/https URL. V1 не принимает mailto, file://, custom schemes, verb и workingDirectory."
                },
                "dryRun": {
                  "type": "boolean",
                  "description": "Если true, invocation запрашивает dry-run path через shared execution gate и safe preview без live ShellExecuteExW call."
                },
                "confirm": {
                  "type": "boolean",
                  "description": "Если true, invocation сообщает shared execution gate, что обязательное user confirmation уже получено."
                }
              },
              "required": ["targetKind", "target"]
            }
            """);

        return document.RootElement.Clone();
    }
}
