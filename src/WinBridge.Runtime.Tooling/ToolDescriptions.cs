namespace WinBridge.Runtime.Tooling;

public static class ToolDescriptions
{
    public const string ComputerUseWinListAppsTool = "Возвращает running Windows apps для Computer Use for Windows. Публичный operator surface группирует visible window instances по app approval identity, публикует selectable `windows[]` с runtime-owned opaque `windowId` и заменяет latest published selector snapshot для следующего `get_app_state`.";
    public const string ComputerUseWinGetAppStateTool = "Начинает или продолжает app use session и возвращает action-ready состояние конкретного window target: screenshot, compact accessibility tree, stateToken, captureReference и warnings. Primary reusable selector — `windowId` из latest `list_apps`; `hwnd` остаётся explicit low-level/debug path и не минтит новый public selector, если target не совпал с current published snapshot. stateToken публикуется только если capture и accessibility tree построены успешно; observation failure отвечает structured `failed` без session commit.";
    public const string ComputerUseWinClickTool = "Кликает по elementIndex или pixel coordinates из последнего app state. При наличии elementIndex runtime сначала пере-подтверждает target через свежий UIA snapshot; coordinate click остаётся low-confidence path и требует explicit confirm.";
    public const string ComputerUseWinTypeTextTool = "Печатает текст в текущий app session через реальный input path без hidden clipboard fallback.";
    public const string ComputerUseWinPressKeyTool = "Нажимает named key literal или modifier combo в текущий app session. Bare printable text сюда не входит и должен идти через type_text.";
    public const string ComputerUseWinSetValueTool = "Семантически устанавливает text или number value у конкретного элемента из последнего app state через ValuePattern/RangeValuePattern без hidden typing fallback.";
    public const string ComputerUseWinScrollTool = "Скроллит app session по elementIndex или point из последнего app state.";
    public const string ComputerUseWinPerformSecondaryActionTool = "Выполняет product-owned secondary action над semantic target из последнего app state.";
    public const string ComputerUseWinDragTool = "Делает drag gesture в app session по element indices или coordinates из последнего app state.";

    public const string OknoHealthTool = "Возвращает сводку состояния runtime и консервативный readiness snapshot: transport, artifacts, implemented tools, display identity path, guard domains и capability status без hidden enforcement.";

    public const string OknoContractTool = "Возвращает текущий MCP contract runtime: implemented tools, deferred tools, execution_policy metadata для policy-bearing public tools и declared deferred tools, а также notes без вызова side effects.";

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

    public const string WindowsCaptureTool = "Выполняет capture выбранной цели и возвращает PNG + structured metadata. При scope=window target выбирается как explicit hwnd или attached window, bounds описывает raster/content basis, frameBounds публикует capture-time live frame basis, а captureReference даёт input-compatible copy-through bridge для capture_pixels, если runtime смог доказать target provenance. При scope=desktop target выбирается как explicit monitorId, explicit hwnd, attached window или primary monitor. Все bounds и pixel sizes выражены в physical_pixels.";
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
    public const string WaitTimeoutMsParameter = "Максимальное время ожидания в миллисекундах. Значение должно быть > 0. По умолчанию используется канонический runtime timeout.";

    public const string WindowsLaunchProcessTool = "Явно запускает executable/process через direct ProcessStartInfo semantics без shell-open, auto-attach и auto-focus. Success фиксирует start/PID, а optional waitForWindow только дополнительно проверяет main window.";
    public const string LaunchProcessExecutableParameter = "Обязательный executable target: fully qualified direct executable path с расширением .exe или .com либо bare executable name для PATH lookup. URL, shell-open target, rooted directory, unsupported file type и relative subpath в текущем contract не поддерживаются.";
    public const string LaunchProcessArgsParameter = "Аргументы запуска как массив строк. Runtime freeze-ит только ArgumentList semantics и не принимает raw command line string.";
    public const string LaunchProcessWorkingDirectoryParameter = "Optional absolute working directory для уже запущенного процесса. Это поле не участвует в executable resolution.";
    public const string LaunchProcessWaitForWindowParameter = "Если true, runtime после успешного старта дополнительно пытается наблюдать non-zero main window handle в пределах timeout. Focus и attach в этот шаг не входят.";
    public const string LaunchProcessTimeoutMsParameter = "Timeout для optional main window observation. Допустим только вместе с waitForWindow=true и должен быть > 0.";
    public const string LaunchProcessDryRunParameter = "Если true, invocation запрашивает dry-run path через shared execution gate и safe preview без Process.Start(...).";
    public const string LaunchProcessConfirmParameter = "Если true, invocation сообщает shared execution gate, что обязательное user confirmation уже получено.";

    public const string WindowsOpenTargetTool = "Запрашивает shell-open для document, folder или http/https URL без смешения с direct process launch, auto-attach и auto-focus. Success фиксирует shell acceptance, а optional handler process id остаётся только best-effort enrichment.";
    public const string OpenTargetKindParameter = "Тип shell-open target. Текущий contract поддерживает только document, folder и url. Поле обязательно и не заменяется эвристикой по строке target.";
    public const string OpenTargetTargetParameter = "Сам target для shell-open. Для document и folder допустим только absolute local/UNC path. Для url допустим только absolute http/https URL. Текущий contract не принимает mailto, file://, custom schemes, verb и workingDirectory.";
    public const string OpenTargetDryRunParameter = "Если true, invocation запрашивает dry-run path через shared execution gate и safe preview без live ShellExecuteExW call.";
    public const string OpenTargetConfirmParameter = "Если true, invocation сообщает shared execution gate, что обязательное user confirmation уже получено.";

    public const string WindowsInputTool = "Public click-first boundary для ordered batch input actions: один tool `windows.input` с `actions[]`, строгой target policy `explicit -> attached`, coordinate spaces `capture_pixels`/`screen`, status model `blocked/needs_confirmation/verify_needed/failed/done` и без hidden focus/capture/dry-run semantics. Текущий shipped subset: `move`, `click`, `double_click`, `click(button=right)`.";
    public const string InputActionsParameter = "Ordered batch input actions. В текущем public click-first subset публикуются только `move`, `click` и `double_click`; правый клик задаётся как `click(button=right)`. Structural slots для `drag`, `scroll`, `type` и `keypress` остаются frozen во внутреннем DTO surface, но не публикуются как shipped schema.";
    public const string InputHwndParameter = "Явный HWND target для window-scoped input. Если не передан, runtime использует только attached window; active-window fallback для input запрещён.";
    public const string InputConfirmParameter = "Если true, invocation сообщает shared execution gate, что обязательное user confirmation уже получено. Public dryRun для windows.input не поддерживается.";
}
