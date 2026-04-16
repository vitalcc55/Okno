namespace WinBridge.Runtime.Windows.Shell;

internal interface IInputAsyncStateReadabilityPlatform
{
    uint GetCurrentThreadId();

    IntPtr GetThreadDesktop(uint threadId);

    bool TryQueryDesktopReceivesInput(IntPtr desktopHandle, out bool receivesInput);

    IntPtr OpenInputDesktop(uint desiredAccess);

    void CloseDesktop(IntPtr hDesktop);
}
