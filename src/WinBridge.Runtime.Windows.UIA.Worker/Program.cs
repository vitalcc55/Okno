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
UiaSnapshotWorkerInvocation invocation = JsonSerializer.Deserialize<UiaSnapshotWorkerInvocation>(payload, jsonOptions)
    ?? throw new InvalidOperationException("UIA worker не получил корректный invocation payload.");

Win32UiAutomationBackend backend = new(TimeProvider.System);
UiaSnapshotBackendResult result = await backend.CaptureAsync(
    invocation.TargetWindow,
    invocation.Request,
    CancellationToken.None).ConfigureAwait(false);

await Console.Out.WriteAsync(JsonSerializer.Serialize(result, jsonOptions)).ConfigureAwait(false);
