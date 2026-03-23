namespace WinBridge.Runtime.Tooling;

public static class ToolDescriptions
{
    public const string OknoHealthTool = "Возвращает сводку состояния runtime: transport, artifacts, implemented tools, active monitor count и состояние display identity path.";

    public const string OknoContractTool = "Возвращает текущий MCP contract runtime: implemented tools, deferred tools и notes без вызова side effects.";

    public const string OknoSessionStateTool = "Возвращает текущий session snapshot, включая attached window и mode без изменения session state.";

    public const string WindowsListMonitorsTool = "Возвращает active monitor targets текущей desktop session вместе с diagnostics display identity path. Используй перед explicit desktop capture по monitorId.";

    public const string WindowsListWindowsTool = "Возвращает live inventory top-level окон. По умолчанию показывает видимые рабочие окна; includeInvisible=true добавляет invisible и untitled windows для diagnostics и target resolution.";
    public const string IncludeInvisibleParameter = "Если true, включает invisible и untitled windows. Это полезно для diagnostics, attach и identity resolution, но делает inventory шумнее.";

    public const string WindowsAttachWindowTool = "Выбирает live window target и прикрепляет его к текущей сессии. Attach требует стабильной identity окна, а не только совпавшего заголовка.";
    public const string HwndParameter = "Явный HWND target. Если передан, имеет приоритет над titlePattern и processName.";
    public const string TitlePatternParameter = "Regex по title окна. Используется, если hwnd не передан. Слишком широкий паттерн может завершиться ambiguous или timeout.";
    public const string ProcessNameParameter = "Имя процесса окна. Используется как selector вместе или вместо titlePattern, если hwnd не передан.";

    public const string WindowsActivateWindowTool = "Делает attached window usable target: при необходимости restore, затем попытка foreground focus и обязательная final live-state verification. Status done означает подтверждённый foreground usable state, а не просто попытку активации.";

    public const string WindowsFocusWindowTool = "Запрашивает foreground focus для explicit hwnd или attached window. В отличие от activate_window не делает restore и не подтверждает usability final-state.";
    public const string FocusHwndParameter = "Явный HWND для focus. Если не передан, используется attached window текущей session.";

    public const string WindowsCaptureTool = "Выполняет capture выбранной цели и возвращает PNG + structured metadata. При scope=window target выбирается как explicit hwnd или attached window. При scope=desktop target выбирается как explicit monitorId, explicit hwnd, attached window или primary monitor. Все bounds и pixel sizes выражены в physical_pixels.";
    public const string CaptureScopeParameter = "Capture scope. window снимает explicit hwnd или attached window. desktop снимает explicit monitorId, explicit hwnd, attached window или primary monitor.";
    public const string CaptureHwndParameter = "Явный HWND target. При scope=window имеет приоритет над attached window. При scope=desktop используется для выбора monitor окна, если monitorId не задан. Для desktop capture нельзя передавать одновременно с monitorId.";
    public const string CaptureMonitorIdParameter = "Явный monitor target для desktop capture. Допустим только при scope=desktop и имеет приоритет над monitor attached window.";

    public const string WindowsUiaSnapshotTool = "Возвращает UIA snapshot выбранного окна в control view. Target policy: explicit hwnd -> attached window -> active foreground top-level window. Tool не активирует окно скрыто и возвращает structured metadata + text payload без image block.";
    public const string UiaSnapshotHwndParameter = "Явный HWND для UIA snapshot. Если передан, имеет приоритет над attached и active target. Stale или invalid explicit hwnd не fallback-ится и даёт targetFailureCode=stale_explicit_target.";
    public const string UiaSnapshotDepthParameter = "Максимальная глубина обхода control view. Значение должно быть >= 0. По умолчанию используется канонический UIA snapshot depth.";
    public const string UiaSnapshotMaxNodesParameter = "Максимальный node budget для materialized subtree. Значение должно быть в диапазоне 1..1024. При достижении budget result честно помечает truncated и nodeBudgetBoundaryReached.";

    public const string WindowsWaitTool = "Ждёт наступления live condition для explicit, attached или active окна. Public contract совпадает с runtime wait model: condition + nested selector + expectedText + hwnd + timeoutMs, а result возвращает structured wait payload без image block.";
    public const string WaitConditionParameter = "Тип wait condition. Поддерживаются: active_window_matches, element_exists, element_gone, text_appears, visual_changed, focus_is.";
    public const string WaitSelectorParameter = "Nested selector для element/text/focus waits. Передавай object с полями name, automationId и/или controlType. Для active_window_matches и visual_changed selector не нужен.";
    public const string WaitExpectedTextParameter = "Ожидаемый текст для condition text_appears. Для остальных conditions должен быть пустым.";
    public const string WaitHwndParameter = "Явный HWND target. Если передан, имеет приоритет над attached и active target. Stale или invalid explicit hwnd не fallback-ится и даёт targetFailureCode=stale_explicit_target.";
    public const string WaitTimeoutMsParameter = "Максимальное время ожидания в миллисекундах. Значение должно быть > 0. По умолчанию используется wait timeout V1.";
}
