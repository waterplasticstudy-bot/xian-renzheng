using System;
using System.IO;
using System.Windows.Forms;

namespace XianAuth
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            DailyLog.Paths = AppPaths.ForBase();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs args)
            {
                try { DailyLog.Error(args.Exception.Message); } catch (Exception) { }
                MessageBox.Show("出现错误：" + args.Exception.Message, "闲·认证", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
        }
    }
}
