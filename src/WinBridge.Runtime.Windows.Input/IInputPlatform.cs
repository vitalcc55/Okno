// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Runtime.Windows.Input;

internal interface IInputPlatform
{
    InputProcessSecurityContext ProbeCurrentProcessSecurity();

    InputTargetSecurityInfo ProbeTargetSecurity(long hwnd, int? processIdHint);

    InputPointerSideEffectBoundaryResult ValidatePointerSideEffectBoundary(WindowDescriptor admittedTargetWindow);

    bool TrySetCursorPosition(InputPoint screenPoint);

    bool TryGetCursorPosition(out InputPoint screenPoint);

    InputClickDispatchResult DispatchClick(InputClickDispatchContext context);

    InputDispatchResult DispatchText(InputTextDispatchContext context);

    InputDispatchResult DispatchKeypress(InputKeypressDispatchContext context);

    InputDispatchResult DispatchScroll(InputScrollDispatchContext context);

    InputDispatchResult DispatchDrag(InputDragDispatchContext context);
}
