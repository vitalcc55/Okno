// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using WinBridge.Runtime.Contracts;

namespace WinBridge.Server.ComputerUse;

internal sealed record ComputerUseWinFailureTranslation(string? FailureCode, string? Reason);

internal static class ComputerUseWinFailureCodeMapper
{
    public static string? ToPublicFailureCode(string? failureCode) =>
        failureCode switch
        {
            null or "" => null,
            InputFailureCodeValues.InvalidRequest => ComputerUseWinFailureCodeValues.InvalidRequest,
            InputFailureCodeValues.UnsupportedCoordinateSpace => ComputerUseWinFailureCodeValues.InvalidRequest,
            InputFailureCodeValues.UnsupportedActionType => ComputerUseWinFailureCodeValues.UnsupportedAction,
            InputFailureCodeValues.UnsupportedKey => ComputerUseWinFailureCodeValues.UnsupportedAction,
            InputFailureCodeValues.UnsupportedKeyboardLayout => ComputerUseWinFailureCodeValues.UnsupportedAction,
            InputFailureCodeValues.MissingTarget => ComputerUseWinFailureCodeValues.MissingTarget,
            InputFailureCodeValues.StaleExplicitTarget => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.StaleAttachedTarget => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.CaptureReferenceRequired => ComputerUseWinFailureCodeValues.CaptureReferenceRequired,
            InputFailureCodeValues.CaptureReferenceStale => ComputerUseWinFailureCodeValues.StaleState,
            InputFailureCodeValues.TargetPreflightFailed => ComputerUseWinFailureCodeValues.TargetPreflightFailed,
            InputFailureCodeValues.TargetNotForeground => ComputerUseWinFailureCodeValues.TargetNotForeground,
            InputFailureCodeValues.TargetMinimized => ComputerUseWinFailureCodeValues.TargetMinimized,
            InputFailureCodeValues.TargetIntegrityBlocked => ComputerUseWinFailureCodeValues.TargetIntegrityBlocked,
            InputFailureCodeValues.PointOutOfBounds => ComputerUseWinFailureCodeValues.PointOutOfBounds,
            InputFailureCodeValues.CursorMoveFailed => ComputerUseWinFailureCodeValues.CursorMoveFailed,
            InputFailureCodeValues.InputDispatchFailed => ComputerUseWinFailureCodeValues.InputDispatchFailed,
            _ when IsPublicFailureCode(failureCode) => failureCode,
            _ => ComputerUseWinFailureCodeValues.InputDispatchFailed,
        };

    public static ComputerUseWinFailureTranslation ToPublicFailure(string? failureCode, string? rawReason)
    {
        string? publicFailureCode = ToPublicFailureCode(failureCode);
        if (publicFailureCode is null)
        {
            return new(null, rawReason);
        }

        return new(publicFailureCode, CreatePublicReason(publicFailureCode, rawReason));
    }

    private static bool IsPublicFailureCode(string? failureCode) =>
        failureCode is
            ComputerUseWinFailureCodeValues.InvalidRequest or
            ComputerUseWinFailureCodeValues.MissingTarget or
            ComputerUseWinFailureCodeValues.AmbiguousTarget or
            ComputerUseWinFailureCodeValues.ApprovalRequired or
            ComputerUseWinFailureCodeValues.BlockedTarget or
            ComputerUseWinFailureCodeValues.IdentityProofUnavailable or
            ComputerUseWinFailureCodeValues.StateRequired or
            ComputerUseWinFailureCodeValues.StaleState or
            ComputerUseWinFailureCodeValues.ObservationFailed or
            ComputerUseWinFailureCodeValues.UnsupportedAction or
            ComputerUseWinFailureCodeValues.UnexpectedInternalFailure or
            ComputerUseWinFailureCodeValues.CaptureReferenceRequired or
            ComputerUseWinFailureCodeValues.TargetPreflightFailed or
            ComputerUseWinFailureCodeValues.TargetNotForeground or
            ComputerUseWinFailureCodeValues.TargetMinimized or
            ComputerUseWinFailureCodeValues.TargetIntegrityBlocked or
            ComputerUseWinFailureCodeValues.PointOutOfBounds or
            ComputerUseWinFailureCodeValues.CursorMoveFailed or
            ComputerUseWinFailureCodeValues.InputDispatchFailed;

    private static string CreatePublicReason(string failureCode, string? rawReason) =>
        failureCode switch
        {
            ComputerUseWinFailureCodeValues.InvalidRequest =>
                "Запрос больше не соответствует публичному action contract; проверь аргументы и повтори вызов с актуальным stateToken.",
            ComputerUseWinFailureCodeValues.UnsupportedAction =>
                "Computer Use for Windows получил неподдерживаемый action outcome; повтори сценарий через актуальный публичный contract.",
            ComputerUseWinFailureCodeValues.UnexpectedInternalFailure =>
                "Computer Use for Windows столкнулся с unexpected internal failure до подтверждённого runtime action outcome.",
            ComputerUseWinFailureCodeValues.IdentityProofUnavailable =>
                "Computer Use for Windows не смог подтвердить стабильную process identity окна; повтори get_app_state после нового live proof.",
            ComputerUseWinFailureCodeValues.MissingTarget =>
                "Целевое окно больше не найдено; заново вызови get_app_state и повтори действие только после нового stateToken.",
            ComputerUseWinFailureCodeValues.StaleState =>
                "Состояние окна устарело; заново вызови get_app_state и используй свежий stateToken перед retry.",
            ComputerUseWinFailureCodeValues.ObservationFailed =>
                "Computer Use for Windows не смог materialize fresh observation state; заново вызови get_app_state перед следующим action.",
            ComputerUseWinFailureCodeValues.CaptureReferenceRequired =>
                "Для coordinate action по screenshot coordinates нужен актуальный get_app_state со свежим capture proof.",
            ComputerUseWinFailureCodeValues.TargetPreflightFailed or ComputerUseWinFailureCodeValues.TargetNotForeground or ComputerUseWinFailureCodeValues.TargetMinimized =>
                "Окно изменило live activation state до dispatch; перед retry сначала заново вызови get_app_state.",
            ComputerUseWinFailureCodeValues.TargetIntegrityBlocked =>
                "Windows заблокировала input к целевому окну из-за integrity boundary.",
            ComputerUseWinFailureCodeValues.PointOutOfBounds =>
                "Координаты больше не соответствуют текущему окну; заново вызови get_app_state перед повтором.",
            ComputerUseWinFailureCodeValues.CursorMoveFailed =>
                "Windows не подтвердила позиционирование указателя; перед retry сначала обнови состояние через get_app_state.",
            ComputerUseWinFailureCodeValues.InputDispatchFailed =>
                "Windows не подтвердила выполнение действия; перед повтором сначала перепроверь состояние приложения через get_app_state.",
            _ => rawReason ?? "Computer Use for Windows завершился structured failure.",
        };
}
