namespace WinBridge.Runtime.Diagnostics;

public interface IAuditPayloadRedactor
{
    AuditRedactionResult Redact(AuditPayloadRedactionContext context, object? payload);
}
