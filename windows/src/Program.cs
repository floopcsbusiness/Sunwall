using System;
using System.Threading;
using System.Windows.Forms;

namespace Sunwall
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Log.Init();
            Log.Write("start");

            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Report("Something went wrong while running", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Log.Write("unhandled: " + e.ExceptionObject);
                };

                try { Native.SetProcessDPIAware(); }
                catch (Exception) { /* Older Windows: the windows still work, just scaled. */ }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SunwallApp(args));
            }
            catch (Exception ex)
            {
                Report("Sunwall could not start", ex);
            }
        }

        static void Report(string headline, Exception ex)
        {
            Log.Write(headline + ": " + ex);
            try
            {
                MessageBox.Show(
                    headline + ".\n\n" + ex.GetType().Name + ": " + ex.Message
                    + "\n\nA log was written to:\n" + Log.FilePath,
                    "Sunwall", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }
    }
}
