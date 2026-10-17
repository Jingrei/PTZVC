using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("PTZVC")]
[assembly: AssemblyDescription("PTZVC - PTZ Virtual Controller")]
[assembly: AssemblyProduct("PTZVC (PTZ Virtual Controller)")]
[assembly: AssemblyCopyright("PTZVC")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PTZVC
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                DarkMessage.Show(null, "PTZVC error", "Something went wrong:\n\n" + e.Exception.Message);
            };

            if (Array.IndexOf(args, "--virtual-camera") >= 0)
                Application.Run(new VirtualCameraForm(args));
            else
                Application.Run(new MainForm());
        }
    }
}
