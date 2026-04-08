namespace WinBridge.Runtime.Contracts;

public static class OpenTargetFailureCodeValues
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedTargetKind = "unsupported_target_kind";
    public const string UnsupportedUriScheme = "unsupported_uri_scheme";
    public const string TargetNotFound = "target_not_found";
    public const string TargetAccessDenied = "target_access_denied";
    public const string NoAssociation = "no_association";
    public const string ShellRejectedTarget = "shell_rejected_target";
}
