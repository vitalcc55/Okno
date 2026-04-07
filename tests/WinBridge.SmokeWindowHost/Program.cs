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
        private readonly TextBox _queryTextBox;
        private readonly Panel _visualHeartbeatPanel;
        private readonly Label _visualHeartbeatLabel;
        private readonly System.Windows.Forms.Timer _visualHeartbeatTimer;
        private readonly System.Windows.Forms.Timer _autoCloseTimer;
        private readonly System.Windows.Forms.Timer _transientHideTimer;
        private readonly int _visualBurstTransitionCount;
        private int _remainingVisualTransitions;
        private bool _visualPhase;

        public SmokeWindowForm(string title, int lifetimeMs, int visualBurstMs)
        {
            Text = title;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(120, 120, 520, 360);
            ShowInTaskbar = true;
            _runButton = CreateRunButton();
            _transientWaitButton = CreateTransientWaitButton();
            _queryTextBox = CreateQueryTextBox();
            _visualHeartbeatPanel = CreateVisualHeartbeatPanel();
            _visualHeartbeatLabel = CreateVisualHeartbeatLabel();
            _visualHeartbeatTimer = CreateVisualHeartbeatTimer();
            _autoCloseTimer = CreateAutoCloseTimer(lifetimeMs);
            _transientHideTimer = CreateTransientHideTimer();
            _visualBurstTransitionCount = Math.Max(1, (int)Math.Ceiling((double)visualBurstMs / VisualHeartbeatIntervalMs));
            Controls.Add(_runButton);
            Controls.Add(_transientWaitButton);
            Controls.Add(CreateRememberCheckBox());
            Controls.Add(_queryTextBox);
            Controls.Add(CreateTreeView());
            Controls.Add(_visualHeartbeatPanel);
            Controls.Add(_visualHeartbeatLabel);
            Shown += (_, _) =>
            {
                QueueCanonicalFocusTarget();
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
                AccessibleName = "Remember semantic selection",
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
                Bounds = new Rectangle(24, 184, 220, 116),
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
            Bounds = _visualPhase
                ? new Rectangle(120, 120, 560, 360)
                : new Rectangle(120, 120, 520, 360);
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
}
