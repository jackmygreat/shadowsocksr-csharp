using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using Microsoft.Win32;
using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Util;
using Timer = System.Timers.Timer;
using Shadowsocks.View;

namespace Shadowsocks
{
    internal static class Program
    {
        private static ShadowsocksController _controller;
        private static MenuViewController _viewController;

        private static int exited;

        /// <summary>
        ///     应用程序的主入口点。
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            foreach (var arg in args)
                if (arg == "--setautorun")
                {
                    if (!AutoStartup.Switch()) Environment.ExitCode = 1;
                    return;
                }

            using (var mutex = new Mutex(false, "Global\\ShadowsocksR_" + Application.StartupPath.GetHashCode()))
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.EnableVisualStyles();
                Application.ApplicationExit += Application_ApplicationExit;
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
                Application.SetCompatibleTextRenderingDefault(false);

                if (!mutex.WaitOne(0, false))
                {
                    MessageBox.Show(I18N.GetString("Find Shadowsocks icon in your notify tray.") + "\n" +
                                    I18N.GetString(
                                        "If you want to start multiple Shadowsocks, make a copy in another directory."),
                        I18N.GetString("ShadowsocksR is already running."));
                    return;
                }

                Directory.SetCurrentDirectory(Application.StartupPath);

                var try_times = 0;
                while (Configuration.Load() == null) //gui-config.json
                {
                    if (try_times >= 5)
                        return;
                    using (var dlg = new InputPassword())
                    {
                        if (dlg.ShowDialog() == DialogResult.OK)
                            Configuration.SetPassword(dlg.password);
                        else
                            return;
                    }

                    try_times += 1;
                }

                if (try_times > 0)
                    Logging.save_to_file = false;

                _controller = new ShadowsocksController();
                HostMap.Instance().LoadHostFile();

                // Logging
                Configuration cfg = _controller.GetConfiguration();
                Logging.save_to_file = cfg.logEnable;

                //#if !DEBUG
                Logging.OpenLogFile();
                //#endif

                // Enable Modern TLS when .NET 4.5+ installed.
                if (EnvCheck.CheckDotNet45())
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType) 3072;
                
                
                _viewController = new MenuViewController(_controller);

                _controller.Start();

                //Util.Utils.ReleaseMemory();

                Application.Run();
            }

            Console.ReadLine();
            _controller.Stop();
        }

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    if (_controller != null)
                    {
                        var timer = new Timer(5 * 1000);
                        timer.Elapsed += Timer_Elapsed;
                        timer.AutoReset = false;
                        timer.Enabled = true;
                        timer.Start();
                    }

                    break;
                case PowerModes.Suspend:
                    if (_controller != null) _controller.Stop();
                    break;
            }
        }

        private static void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (_controller != null) _controller.Start();
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
            }
            finally
            {
                try
                {
                    var timer = (Timer) sender;
                    timer.Enabled = false;
                    timer.Stop();
                    timer.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.LogUsefulException(ex);
                }
            }
        }

        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            if (_controller != null) _controller.Stop();
            _controller = null;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (Interlocked.Increment(ref exited) == 1)
            {
                Logging.Log(LogLevel.Error, e.ExceptionObject != null ? e.ExceptionObject.ToString() : "");
                MessageBox.Show(I18N.GetString("Unexpected error, ShadowsocksR will exit.") +
                                Environment.NewLine + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : ""),
                    "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }
    }
}