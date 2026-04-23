namespace WinBridge.Server.ComputerUse;

internal sealed class ComputerUseWinInstructionUnavailableException : Exception
{
    public ComputerUseWinInstructionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
