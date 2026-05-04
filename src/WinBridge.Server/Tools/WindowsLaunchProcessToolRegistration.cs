// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Server.Tools;

internal static class WindowsLaunchProcessToolRegistration
{
    public static McpServerTool Create(Func<WindowTools> getWindowTools)
    {
        ArgumentNullException.ThrowIfNull(getWindowTools);

        ValueTask<CallToolResult> Handler(
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken) =>
            new(getWindowTools().LaunchProcess(requestContext, cancellationToken));

        McpServerTool tool = McpServerTool.Create(
            (Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>>)Handler,
            new McpServerToolCreateOptions
            {
                Name = ToolNames.WindowsLaunchProcess,
                Description = ToolDescriptions.WindowsLaunchProcessTool,
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
                "executable": {
                  "type": "string",
                  "description": "Обязательный executable target: fully qualified direct executable path с расширением .exe или .com либо bare executable name для PATH lookup. URL, shell-open target, rooted directory, unsupported file type и relative subpath в V1 не поддерживаются."
                },
                "args": {
                  "type": ["array", "null"],
                  "description": "Аргументы запуска как массив строк. Runtime freeze-ит только ArgumentList semantics и не принимает raw command line string.",
                  "items": {
                    "type": "string"
                  }
                },
                "workingDirectory": {
                  "type": ["string", "null"],
                  "description": "Optional absolute working directory для уже запущенного процесса. Это поле не участвует в executable resolution."
                },
                "waitForWindow": {
                  "type": "boolean",
                  "description": "Если true, runtime после успешного старта дополнительно пытается наблюдать non-zero main window handle в пределах timeout. Focus и attach в этот шаг не входят."
                },
                "timeoutMs": {
                  "type": ["integer", "null"],
                  "description": "Timeout для optional main window observation. Допустим только вместе с waitForWindow=true и должен быть > 0."
                },
                "dryRun": {
                  "type": "boolean",
                  "description": "Если true, invocation запрашивает dry-run path через shared execution gate и safe preview без Process.Start(...)."
                },
                "confirm": {
                  "type": "boolean",
                  "description": "Если true, invocation сообщает shared execution gate, что обязательное user confirmation уже получено."
                }
              },
              "required": ["executable"]
            }
            """);

        return document.RootElement.Clone();
    }
}
