// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using System.Text.Json;
using WinBridge.Runtime.Windows.UIA;

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

JsonSerializerOptions jsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
};

string payload = await Console.In.ReadToEndAsync();
UiAutomationWorkerInvocation invocation = JsonSerializer.Deserialize<UiAutomationWorkerInvocation>(payload, jsonOptions)
    ?? throw new InvalidOperationException("UIA worker не получил корректный invocation payload.");

object result = invocation.Operation switch
{
    UiAutomationWorkerOperationValues.Snapshot when invocation.SnapshotRequest is not null =>
        await new Win32UiAutomationBackend(TimeProvider.System)
            .CaptureAsync(
                invocation.TargetWindow,
                invocation.SnapshotRequest,
                CancellationToken.None)
            .ConfigureAwait(false),
    UiAutomationWorkerOperationValues.WaitProbe when invocation.WaitProbeRequest is not null =>
        await new Win32UiAutomationWaitProbe()
            .ProbeAsync(
                invocation.TargetWindow,
                invocation.WaitProbeRequest,
                Timeout.InfiniteTimeSpan,
                CancellationToken.None)
            .ConfigureAwait(false),
    _ => throw new InvalidOperationException("UIA worker получил unsupported operation payload."),
};

await Console.Out.WriteAsync(JsonSerializer.Serialize(result, jsonOptions)).ConfigureAwait(false);
