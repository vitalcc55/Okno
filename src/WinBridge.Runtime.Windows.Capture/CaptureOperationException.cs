namespace WinBridge.Runtime.Windows.Capture;

public sealed class CaptureOperationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
