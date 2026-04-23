namespace WinBridge.Server.ComputerUse;

internal enum ComputerUseWinActionLifecyclePhase
{
    BeforeActivation,
    AfterActivationBeforeDispatch,
    AfterRevalidationBeforeDispatch,
    PostDispatch,
}
