using System.Drawing;
using System.Windows.Forms;

namespace WinBridge.SmokeWindowHost;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string title = ParseTitle(args) ?? "Okno Smoke Window";
        ApplicationConfiguration.Initialize();
        Application.Run(new SmokeWindowForm(title));
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

    private sealed class SmokeWindowForm : Form
    {
        public SmokeWindowForm(string title)
        {
            Text = title;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(120, 120, 520, 360);
            ShowInTaskbar = true;
            Controls.Add(CreateRunButton());
            Controls.Add(CreateRememberCheckBox());
            Controls.Add(CreateQueryTextBox());
            Controls.Add(CreateTreeView());
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
                Bounds = new Rectangle(24, 72, 220, 24),
                Checked = true,
            };

        private static TextBox CreateQueryTextBox() =>
            new()
            {
                Name = "SmokeQueryInputTextBox",
                AccessibleName = "Smoke query input",
                Text = "semantic text",
                Bounds = new Rectangle(24, 112, 220, 28),
            };

        private static TreeView CreateTreeView()
        {
            TreeView treeView = new()
            {
                Name = "SmokeNavigationTree",
                AccessibleName = "Smoke navigation tree",
                Bounds = new Rectangle(24, 160, 220, 140),
                HideSelection = false,
            };

            TreeNode workspaceNode = new("Workspace");
            workspaceNode.Nodes.Add("Inbox");
            workspaceNode.Nodes.Add("Archive");
            treeView.Nodes.Add(workspaceNode);
            treeView.ExpandAll();
            return treeView;
        }
    }
}
