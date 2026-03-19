namespace WinBridge.Runtime.Windows.UIA;

internal interface IUiaSnapshotNode
{
    UiaSnapshotNodeData GetData();

    IUiaSnapshotNode? GetFirstChild();

    IUiaSnapshotNode? GetNextSibling();
}
