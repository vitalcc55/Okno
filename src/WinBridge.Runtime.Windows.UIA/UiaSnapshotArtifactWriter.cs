using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinBridge.Runtime.Contracts;
using WinBridge.Runtime.Diagnostics;

namespace WinBridge.Runtime.Windows.UIA;

internal sealed class UiaSnapshotArtifactWriter(AuditLogOptions auditLogOptions)
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public string Write(UiaSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            string directory = Path.Combine(auditLogOptions.RunDirectory, "uia");
            Directory.CreateDirectory(directory);

            string handle = result.Window?.Hwnd.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            string fileName = UiaSnapshotArtifactNameBuilder.Create("window", handle, result.CapturedAtUtc.UtcDateTime);
            string path = Path.Combine(directory, fileName);
            string payload = JsonSerializer.Serialize(result with { ArtifactPath = path }, JsonOptions);
            File.WriteAllText(path, payload, FileEncoding);
            return path;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UiaSnapshotArtifactException("Runtime не смог записать UIA snapshot artifact на диск.", exception);
        }
        catch (IOException exception)
        {
            throw new UiaSnapshotArtifactException("Runtime не смог записать UIA snapshot artifact на диск.", exception);
        }
    }
}
