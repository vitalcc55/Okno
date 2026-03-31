using WinBridge.Runtime.Guards;
using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Diagnostics;

internal sealed class AuditToolContext
{
    private static readonly Dictionary<string, ToolExecutionRedactionClass> InternalRedactionClassMap =
        new Dictionary<string, ToolExecutionRedactionClass>(StringComparer.Ordinal)
        {
            [ToolNames.WindowsWait] = ToolExecutionRedactionClass.TextPayload,
            [ToolNames.WindowsUiaSnapshot] = ToolExecutionRedactionClass.TargetMetadata,
        };

    private AuditToolContext(
        string toolName,
        ToolExecutionPolicyDescriptor? executionPolicy,
        ToolExecutionRedactionClass redactionClass)
    {
        ToolName = toolName;
        ExecutionPolicy = executionPolicy;
        RedactionClass = redactionClass;
    }

    public string ToolName { get; }

    public ToolExecutionPolicyDescriptor? ExecutionPolicy { get; }

    public ToolExecutionRedactionClass RedactionClass { get; }

    public ToolExecutionDecision? Decision { get; private set; }

    public static AuditToolContext Resolve(string toolName, ToolExecutionPolicyDescriptor? executionPolicy = null)
    {
        ToolExecutionPolicyDescriptor? resolvedPolicy = executionPolicy ?? ToolContractManifest.ResolveExecutionPolicy(toolName);

        ToolExecutionRedactionClass redactionClass =
            resolvedPolicy?.RedactionClass
            ?? (InternalRedactionClassMap.TryGetValue(toolName, out ToolExecutionRedactionClass mappedClass)
                ? mappedClass
                : ToolExecutionRedactionClass.None);

        return new AuditToolContext(toolName, resolvedPolicy, redactionClass);
    }

    public void SetDecision(ToolExecutionDecision decision)
    {
        Decision = decision;
    }
}
