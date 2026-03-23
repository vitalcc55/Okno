namespace WinBridge.Runtime.Waiting;

internal sealed class WaitArtifactException(string message, Exception innerException) : Exception(message, innerException)
{
}
