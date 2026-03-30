using WinBridge.Runtime.Tooling;

namespace WinBridge.Runtime.Guards;

public interface IToolExecutionGate
{
    ToolExecutionDecision Evaluate(ToolExecutionPolicyDescriptor policy, ToolExecutionIntent intent);
}
