// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

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
