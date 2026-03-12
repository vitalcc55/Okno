using System.Diagnostics;

namespace WinBridge.Runtime.Diagnostics;

public static class AuditConstants
{
    public const string SchemaVersion = "1.0.0";

    public const string ServiceName = "Okno";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
}
