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
            Bounds = new Rectangle(120, 120, 420, 280);
            ShowInTaskbar = true;

            Controls.Add(
                new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "Okno smoke window",
                    TextAlign = ContentAlignment.MiddleCenter,
                });
        }
    }
}
