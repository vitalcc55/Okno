namespace WinBridge.Server.ComputerUse;

internal interface IComputerUseWinInstructionProvider
{
    IReadOnlyList<string> GetInstructions(string? processName);
}
