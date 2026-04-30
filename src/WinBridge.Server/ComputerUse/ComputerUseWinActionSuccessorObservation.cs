using ModelContextProtocol.Protocol;
using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinActionSuccessorObservation(
    ComputerUseWinGetAppStateResult? SuccessorState,
    ImageContentBlock? ImageContent,
    ComputerUseWinActionSuccessorStateFailure? Failure)
{
    public static ComputerUseWinActionSuccessorObservation Success(ComputerUseWinMaterializedAppState state) =>
        new(state.Payload, state.ImageContent, null);

    public static ComputerUseWinActionSuccessorObservation Failed(ComputerUseWinActionSuccessorStateFailure failure) =>
        new(null, null, failure);
}
