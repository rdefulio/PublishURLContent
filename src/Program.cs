using System;
using System.Windows.Forms;

namespace PublishContent
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ShowFatalError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                ShowFatalError(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));

            if (!Environment.Is64BitProcess)
            {
                MessageBox.Show(
                    "PublishContent must run as a 64-bit process." + Environment.NewLine + Environment.NewLine +
                    "Citrix Virtual Apps and Desktops 2402 LTSR ships 64-bit PowerShell modules only. " +
                    "A 32-bit process loading those modules crashes immediately (0xc0000005) with no window." + Environment.NewLine + Environment.NewLine +
                    "Rebuild this project as x64 with Prefer32Bit disabled, then copy the new executable to the Delivery Controller.",
                    "Unsupported architecture",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm());
        }

        private static void ShowFatalError(Exception ex)
        {
            MessageBox.Show(
                ex?.ToString() ?? "Unknown error",
                "Publish Content",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
