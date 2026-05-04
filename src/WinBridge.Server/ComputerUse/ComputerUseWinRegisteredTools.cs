// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections;
using ModelContextProtocol.Server;

namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinRegisteredTools : IReadOnlyList<McpServerTool>
{
    private Func<ComputerUseWinTools>? getToolHost;
    private readonly IReadOnlyList<McpServerTool> tools;

    public ComputerUseWinRegisteredTools()
    {
        tools = ComputerUseWinToolRegistration.Create(() =>
            (getToolHost ?? throw new InvalidOperationException("ComputerUseWinTools service is not bound yet.")).Invoke());
    }

    public int Count => tools.Count;

    public McpServerTool this[int index] => tools[index];

    public void BindToolHost(ComputerUseWinTools toolHost)
    {
        ArgumentNullException.ThrowIfNull(toolHost);
        getToolHost = () => toolHost;
    }

    public IEnumerator<McpServerTool> GetEnumerator() => tools.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
