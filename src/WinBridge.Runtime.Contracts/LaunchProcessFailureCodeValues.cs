// SPDX-FileCopyrightText: 2025–2026 Власов Виталий Андреевич <vital.cc55@gmail.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace WinBridge.Runtime.Contracts;

public static class LaunchProcessFailureCodeValues
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedTargetKind = "unsupported_target_kind";
    public const string UnsupportedEnvironmentOverrides = "unsupported_environment_overrides";
    public const string ExecutableNotFound = "executable_not_found";
    public const string WorkingDirectoryNotFound = "working_directory_not_found";
    public const string StartFailed = "start_failed";
    public const string ProcessObjectUnavailable = "process_object_unavailable";
    public const string ProcessExitedBeforeWindow = "process_exited_before_window";
    public const string MainWindowTimeout = "main_window_timeout";
    public const string MainWindowNotObserved = "main_window_not_observed";
    public const string MainWindowObservationNotSupported = "main_window_observation_not_supported";
}
