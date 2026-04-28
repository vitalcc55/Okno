using System.Drawing;
using System.Windows.Forms;

namespace WinBridge.SmokeWindowHost;

internal static class Program
{
    private const int DefaultLifetimeMs = 90000;
    private const int DefaultVisualBurstMs = 6000;

    [STAThread]
    private static void Main(string[] args)
    {
        string title = ParseTitle(args) ?? "Okno Smoke Window";
        int lifetimeMs = ParseLifetimeMs(args) ?? DefaultLifetimeMs;
        int visualBurstMs = ParseVisualBurstMs(args) ?? DefaultVisualBurstMs;
        ApplicationConfiguration.Initialize();
        Application.Run(new SmokeWindowForm(title, lifetimeMs, visualBurstMs));
    }

    private static string? ParseTitle(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--title" && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int? ParseLifetimeMs(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--lifetime-ms"
                && index + 1 < args.Length
                && int.TryParse(args[index + 1], out int lifetimeMs)
                && lifetimeMs > 0)
            {
                return lifetimeMs;
            }
        }

        return null;
    }

    private static int? ParseVisualBurstMs(string[] args)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--visual-burst-ms"
                && index + 1 < args.Length
                && int.TryParse(args[index + 1], out int visualBurstMs)
                && visualBurstMs > 0)
            {
                return visualBurstMs;
            }
        }

        return null;
    }

    private sealed class SmokeWindowForm : Form
    {
        private const int WmAppArmElementGone = 0x8001;
        private const int WmAppPrepareFocus = 0x8002;
        private const int WmAppArmVisualHeartbeat = 0x8003;
        private const int VisualHeartbeatIntervalMs = 500;

        private readonly Button _runButton;
        private readonly Button _transientWaitButton;
        private readonly CheckBox _rememberCheckBox;
        private readonly TextBox _queryTextBox;
        private readonly Label _queryMirrorLabel;
        private readonly NumericUpDown _rangeInput;
        private readonly Label _rangeMirrorLabel;
        private readonly MirrorScrollListBox _scrollListBox;
        private readonly Label _scrollMirrorLabel;
        private readonly Button _dragSourceButton;
        private readonly Panel _dragDestinationPanel;
        private readonly Label _dragMirrorLabel;
        private readonly Panel _visualHeartbeatPanel;
        private readonly Label _visualHeartbeatLabel;
        private readonly System.Windows.Forms.Timer _dragPollTimer;
        private readonly System.Windows.Forms.Timer _visualHeartbeatTimer;
        private readonly System.Windows.Forms.Timer _autoCloseTimer;
        private readonly System.Windows.Forms.Timer _transientHideTimer;
        private readonly int _visualBurstTransitionCount;
        private readonly Point _dragSourceOrigin;
        private Point _dragPointerOffset;
        private int _remainingVisualTransitions;
        private bool _visualPhase;
        private bool _dragInProgress;

        public SmokeWindowForm(string title, int lifetimeMs, int visualBurstMs)
        {
            Text = title;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(120, 120, 520, 560);
            ShowInTaskbar = true;
            _runButton = CreateRunButton();
            _transientWaitButton = CreateTransientWaitButton();
            _rememberCheckBox = CreateRememberCheckBox();
            _queryTextBox = CreateQueryTextBox();
            _queryMirrorLabel = CreateQueryMirrorLabel(_queryTextBox.Text);
            _rangeInput = CreateRangeInput();
            _rangeMirrorLabel = CreateRangeMirrorLabel(_rangeInput.Value);
            _scrollListBox = CreateScrollListBox();
            _scrollMirrorLabel = CreateScrollMirrorLabel(_scrollListBox.TopVisibleItemText);
            _dragSourceButton = CreateDragSourceButton();
            _dragDestinationPanel = CreateDragDestinationPanel();
            _dragMirrorLabel = CreateDragMirrorLabel();
            _dragSourceOrigin = _dragSourceButton.Location;
            _visualHeartbeatPanel = CreateVisualHeartbeatPanel();
            _visualHeartbeatLabel = CreateVisualHeartbeatLabel();
            _dragPollTimer = CreateDragPollTimer();
            _visualHeartbeatTimer = CreateVisualHeartbeatTimer();
            _autoCloseTimer = CreateAutoCloseTimer(lifetimeMs);
            _transientHideTimer = CreateTransientHideTimer();
            _visualBurstTransitionCount = Math.Max(1, (int)Math.Ceiling((double)visualBurstMs / VisualHeartbeatIntervalMs));
            _queryTextBox.TextChanged += (_, _) => UpdateQueryMirror();
            _queryTextBox.Enter += (_, _) => QueueSelectAllInQueryTextBox();
            _queryTextBox.MouseUp += (_, _) => QueueSelectAllInQueryTextBox();
            _rememberCheckBox.CheckedChanged += (_, _) => UpdateRememberSemanticSelectionName();
            _rangeInput.ValueChanged += (_, _) => UpdateRangeMirror();
            _scrollListBox.TopVisibleItemChanged += (_, _) => UpdateScrollMirror();
            _dragSourceButton.MouseDown += DragSourceButtonOnMouseDown;
            Controls.Add(_runButton);
            Controls.Add(_transientWaitButton);
            Controls.Add(_rememberCheckBox);
            Controls.Add(_queryTextBox);
            Controls.Add(_queryMirrorLabel);
            Controls.Add(_rangeInput);
            Controls.Add(_rangeMirrorLabel);
            Controls.Add(CreateTreeView());
            Controls.Add(_visualHeartbeatPanel);
            Controls.Add(_visualHeartbeatLabel);
            Controls.Add(_scrollListBox);
            Controls.Add(_scrollMirrorLabel);
            Controls.Add(_dragSourceButton);
            Controls.Add(_dragDestinationPanel);
            Controls.Add(_dragMirrorLabel);
            Shown += (_, _) =>
            {
                UpdateRememberSemanticSelectionName();
                UpdateScrollMirror();
                ResetDragSurface();
                QueuePrepareCanonicalFocusTarget();
                _autoCloseTimer.Start();
            };
            Activated += (_, _) => QueueCanonicalFocusTarget();
        }

        private static Button CreateRunButton() =>
            new()
            {
                Name = "RunSemanticSmokeButton",
                AccessibleName = "Run semantic smoke",
                Text = "Run semantic smoke",
                Bounds = new Rectangle(24, 24, 180, 34),
                UseVisualStyleBackColor = true,
            };

        private static CheckBox CreateRememberCheckBox() =>
            new()
            {
                Name = "RememberSemanticSelectionCheckBox",
                AccessibleName = "Remember semantic selection: on",
                Text = "Remember semantic selection",
                Bounds = new Rectangle(24, 104, 220, 24),
                Checked = true,
            };

        private static Button CreateTransientWaitButton() =>
            new()
            {
                Name = "TransientWaitTargetButton",
                AccessibleName = "Transient wait target",
                Text = "Transient wait target",
                Bounds = new Rectangle(24, 72, 180, 24),
                UseVisualStyleBackColor = true,
            };

        private static TextBox CreateQueryTextBox() =>
            new()
            {
                Name = "SmokeQueryInputTextBox",
                AccessibleName = "Smoke query input",
                Text = "semantic text",
                Bounds = new Rectangle(24, 136, 220, 28),
            };

        private static Label CreateQueryMirrorLabel(string initialText) =>
            new()
            {
                Name = "QueryMirrorLabel",
                AccessibleName = $"Query mirror: {initialText}",
                Text = $"Query mirror: {initialText}",
                Bounds = new Rectangle(24, 168, 220, 24),
                BackColor = Color.White,
            };

        private static NumericUpDown CreateRangeInput() =>
            new()
            {
                Name = "SmokeRangeInputUpDown",
                AccessibleName = "Smoke range input",
                Minimum = 0,
                Maximum = 10,
                Value = 5,
                Bounds = new Rectangle(24, 196, 220, 24),
            };

        private static Label CreateRangeMirrorLabel(decimal initialValue) =>
            new()
            {
                Name = "RangeMirrorLabel",
                AccessibleName = $"Range mirror: {initialValue}",
                Text = $"Range mirror: {initialValue}",
                Bounds = new Rectangle(24, 224, 220, 24),
                BackColor = Color.White,
            };

        private static MirrorScrollListBox CreateScrollListBox()
        {
            MirrorScrollListBox listBox = new()
            {
                Name = "SmokeScrollListBox",
                AccessibleName = "Smoke scroll list",
                Bounds = new Rectangle(260, 308, 212, 92),
                IntegralHeight = false,
            };
            for (int index = 1; index <= 60; index++)
            {
                listBox.Items.Add($"Scroll item {index:00}");
            }

            return listBox;
        }

        private static Label CreateScrollMirrorLabel(string initialItem) =>
            new()
            {
                Name = "ScrollMirrorLabel",
                AccessibleName = $"Scroll mirror: {initialItem}",
                Text = $"Scroll mirror: {initialItem}",
                Bounds = new Rectangle(260, 404, 212, 24),
                BackColor = Color.White,
            };

        private static Button CreateDragSourceButton() =>
            new()
            {
                Name = "DragSourceTokenButton",
                AccessibleName = "Drag source token",
                Text = "Drag source token",
                Bounds = new Rectangle(24, 436, 140, 36),
                UseVisualStyleBackColor = true,
            };

        private static Panel CreateDragDestinationPanel() =>
            new()
            {
                Name = "DragDestinationTargetPanel",
                AccessibleName = "Drag destination target: empty",
                Bounds = new Rectangle(220, 432, 180, 72),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.LightSteelBlue,
            };

        private static Label CreateDragMirrorLabel() =>
            new()
            {
                Name = "DragMirrorLabel",
                AccessibleName = "Drag mirror: ready",
                Text = "Drag mirror: ready",
                Bounds = new Rectangle(24, 488, 180, 24),
                BackColor = Color.White,
            };

        private static Panel CreateVisualHeartbeatPanel() =>
            new()
            {
                Name = "VisualHeartbeatPanel",
                AccessibleName = "Visual heartbeat panel",
                Bounds = new Rectangle(260, 24, 212, 276),
                BackColor = Color.DarkOliveGreen,
            };

        private static Label CreateVisualHeartbeatLabel() =>
            new()
            {
                Name = "VisualHeartbeatLabel",
                AccessibleName = "Visual heartbeat label",
                Text = "Visual state: A",
                Bounds = new Rectangle(280, 40, 172, 36),
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                BackColor = Color.White,
            };

        private System.Windows.Forms.Timer CreateVisualHeartbeatTimer()
        {
            System.Windows.Forms.Timer timer = new()
            {
                Interval = VisualHeartbeatIntervalMs,
                Enabled = false,
            };
            timer.Tick += (_, _) =>
            {
                ToggleVisualState();
                _remainingVisualTransitions--;
                if (_remainingVisualTransitions <= 0)
                {
                    timer.Stop();
                }
            };
            return timer;
        }

        private System.Windows.Forms.Timer CreateDragPollTimer()
        {
            System.Windows.Forms.Timer timer = new()
            {
                Interval = 16,
                Enabled = false,
            };
            timer.Tick += (_, _) =>
            {
                if (!_dragInProgress)
                {
                    timer.Stop();
                    return;
                }

                Point cursorInForm = PointToClient(Cursor.Position);
                _dragSourceButton.Location = new Point(
                    cursorInForm.X - _dragPointerOffset.X,
                    cursorInForm.Y - _dragPointerOffset.Y);

                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    EndDragInteraction();
                }
            };
            return timer;
        }

        private void UpdateQueryMirror()
        {
            string mirrorText = $"Query mirror: {_queryTextBox.Text}";
            _queryMirrorLabel.Text = mirrorText;
            _queryMirrorLabel.AccessibleName = mirrorText;
        }

        private void UpdateRememberSemanticSelectionName()
        {
            _rememberCheckBox.AccessibleName = _rememberCheckBox.Checked
                ? "Remember semantic selection: on"
                : "Remember semantic selection: off";
        }

        private void SelectAllInQueryTextBox()
        {
            if (!_queryTextBox.IsHandleCreated || !_queryTextBox.Focused)
            {
                return;
            }

            _queryTextBox.SelectAll();
        }

        private void QueueSelectAllInQueryTextBox() =>
            BeginInvoke(SelectAllInQueryTextBox);

        private void UpdateRangeMirror()
        {
            string mirrorText = $"Range mirror: {_rangeInput.Value}";
            _rangeMirrorLabel.Text = mirrorText;
            _rangeMirrorLabel.AccessibleName = mirrorText;
        }

        private void UpdateScrollMirror()
        {
            string mirrorText = $"Scroll mirror: {_scrollListBox.TopVisibleItemText}";
            _scrollMirrorLabel.Text = mirrorText;
            _scrollMirrorLabel.AccessibleName = mirrorText;
        }

        private void DragSourceButtonOnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }

            _dragInProgress = true;
            _dragPointerOffset = eventArgs.Location;
            _dragSourceButton.Capture = true;
            _dragSourceButton.BringToFront();
            _dragPollTimer.Start();
        }

        private void EndDragInteraction()
        {
            if (!_dragInProgress)
            {
                return;
            }

            _dragInProgress = false;
            _dragSourceButton.Capture = false;
            _dragPollTimer.Stop();
            if (_dragDestinationPanel.Bounds.Contains(GetControlCenter(_dragSourceButton)))
            {
                _dragSourceButton.Location = new Point(
                    _dragDestinationPanel.Left + (_dragDestinationPanel.Width - _dragSourceButton.Width) / 2,
                    _dragDestinationPanel.Top + (_dragDestinationPanel.Height - _dragSourceButton.Height) / 2);
                _dragDestinationPanel.AccessibleName = "Drag destination target: occupied";
                UpdateDragMirror("dropped");
                return;
            }

            _dragSourceButton.Location = _dragSourceOrigin;
            _dragDestinationPanel.AccessibleName = "Drag destination target: empty";
            UpdateDragMirror("ready");
        }

        private void ResetDragSurface()
        {
            _dragInProgress = false;
            _dragSourceButton.Capture = false;
            _dragPollTimer.Stop();
            _dragSourceButton.Location = _dragSourceOrigin;
            _dragDestinationPanel.AccessibleName = "Drag destination target: empty";
            UpdateDragMirror("ready");
        }

        private void UpdateDragMirror(string state)
        {
            string mirrorText = $"Drag mirror: {state}";
            _dragMirrorLabel.Text = mirrorText;
            _dragMirrorLabel.AccessibleName = mirrorText;
        }

        private System.Windows.Forms.Timer CreateTransientHideTimer()
        {
            System.Windows.Forms.Timer timer = new()
            {
                Interval = 3500,
                Enabled = false,
            };
            timer.Tick += (_, _) =>
            {
                _transientWaitButton.Visible = false;
                timer.Stop();
            };
            return timer;
        }

        private static System.Windows.Forms.Timer CreateAutoCloseTimer(int lifetimeMs)
        {
            System.Windows.Forms.Timer timer = new()
            {
                Interval = lifetimeMs,
                Enabled = false,
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Application.ExitThread();
            };
            return timer;
        }

        private static TreeView CreateTreeView()
        {
            TreeView treeView = new()
            {
                Name = "SmokeNavigationTree",
                AccessibleName = "Smoke navigation tree",
                Bounds = new Rectangle(24, 252, 220, 48),
                HideSelection = false,
            };

            TreeNode workspaceNode = new("Workspace");
            workspaceNode.Nodes.Add("Inbox");
            workspaceNode.Nodes.Add("Archive");
            treeView.Nodes.Add(workspaceNode);
            treeView.ExpandAll();
            return treeView;
        }

        private void ToggleVisualState()
        {
            _visualPhase = !_visualPhase;
            _visualHeartbeatPanel.BackColor = _visualPhase ? Color.OrangeRed : Color.DarkOliveGreen;
            _visualHeartbeatLabel.Text = _visualPhase ? "Visual state: B" : "Visual state: A";
        }

        private void SetCanonicalFocusTarget()
        {
            ActiveControl = _runButton;
            _runButton.Focus();
        }

        private void PrepareCanonicalFocusTarget()
        {
            BringToFront();
            Activate();
            SetCanonicalFocusTarget();
        }

        private void QueueCanonicalFocusTarget() =>
            BeginInvoke(SetCanonicalFocusTarget);

        private void QueuePrepareCanonicalFocusTarget() =>
            BeginInvoke(PrepareCanonicalFocusTarget);

        private static Point GetControlCenter(Control control) =>
            new(
                control.Left + (control.Width / 2),
                control.Top + (control.Height / 2));

        private void ArmElementGoneScenario()
        {
            _transientHideTimer.Stop();
            _transientWaitButton.Visible = true;
            _transientWaitButton.Enabled = true;
            _transientHideTimer.Start();
        }

        private void ArmVisualHeartbeatScenario()
        {
            if (_visualHeartbeatTimer.Enabled)
            {
                return;
            }

            _remainingVisualTransitions = _visualBurstTransitionCount;
            _visualHeartbeatTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WmAppArmElementGone:
                    ArmElementGoneScenario();
                    m.Result = IntPtr.Zero;
                    return;
                case WmAppPrepareFocus:
                    PrepareCanonicalFocusTarget();
                    m.Result = IntPtr.Zero;
                    return;
                case WmAppArmVisualHeartbeat:
                    ArmVisualHeartbeatScenario();
                    m.Result = IntPtr.Zero;
                    return;
            }

            base.WndProc(ref m);
        }
    }

    private sealed class MirrorScrollListBox : ListBox
    {
        private const int WmVscroll = 0x0115;
        private const int WmMousewheel = 0x020A;
        private const int WmMousehwheel = 0x020E;
        private int _lastTopIndex;

        public event EventHandler? TopVisibleItemChanged;

        public string TopVisibleItemText =>
            Items.Count == 0 || TopIndex < 0 || TopIndex >= Items.Count
                ? "none"
                : Items[TopIndex]?.ToString() ?? "none";

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            _lastTopIndex = TopIndex;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg is WmVscroll or WmMousewheel or WmMousehwheel)
            {
                NotifyIfTopVisibleItemChanged();
            }
        }

        private void NotifyIfTopVisibleItemChanged()
        {
            if (TopIndex == _lastTopIndex)
            {
                return;
            }

            _lastTopIndex = TopIndex;
            TopVisibleItemChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
