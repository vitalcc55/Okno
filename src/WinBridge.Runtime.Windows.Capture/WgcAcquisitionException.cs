namespace WinBridge.Runtime.Windows.Capture;

internal sealed class WgcAcquisitionException(string message, Exception? innerException = null)
    : Exception(message, innerException);
