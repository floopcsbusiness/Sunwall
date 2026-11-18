using System;
using System.Windows.Forms;

namespace Sunwall
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Native.SetProcessDPIAware(); }
            catch (Exception) { /* Older Windows: the windows still work, just scaled. */ }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SunwallApp());
        }
    }
}
