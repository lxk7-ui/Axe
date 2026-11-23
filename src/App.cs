using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

[assembly: AssemblyTitle("Axe")]
[assembly: AssemblyProduct("Axe 视频混剪工具")]
[assembly: AssemblyDescription("Axe 视频混剪工具")]
[assembly: AssemblyVersion("2.8.3.0")]
[assembly: AssemblyFileVersion("2.8.3.0")]

namespace VideoMixApp
{
    public class App
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => WriteCrash(e.ExceptionObject as Exception);
            var app = new Application();
            app.DispatcherUnhandledException += (s, e) =>
            {
                WriteCrash(e.Exception);
                MessageBox.Show("发生异常：" + e.Exception.Message);
                e.Handled = true;
            };
            app.Run(new MainWindow());
        }

        // Unexpected errors go to app.log next to the run's other messages, so a problem on another
        // PC can be traced afterwards.
        private static void WriteCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  未处理的异常：" + ex + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
