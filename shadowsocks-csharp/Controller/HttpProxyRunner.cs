using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    internal class HttpProxyRunner
    {
        private static readonly string runningPath;
        private static readonly string _subPath = @"temp";
        private static readonly string _exeNameNoExt = @"/ssr_privoxy";
        private static readonly string _exeName = @"/ssr_privoxy.exe";
        private Process _process;

        static HttpProxyRunner()
        {
            runningPath = Path.Combine(Application.StartupPath, _subPath);
            _exeNameNoExt = Path.GetFileNameWithoutExtension(Utils.GetExecutablePath());
            _exeName = @"/" + _exeNameNoExt + @".exe";
            if (!Directory.Exists(runningPath)) Directory.CreateDirectory(runningPath);
            Kill();
            try
            {
                FileManager.UncompressFile(runningPath + _exeName, Resources.privoxy_exe);
                FileManager.UncompressFile(runningPath + "/mgwz.dll", Resources.mgwz_dll);
            }
            catch (IOException e)
            {
                Logging.LogUsefulException(e);
            }
        }

        public int RunningPort { get; private set; }

        public bool HasExited()
        {
            if (_process == null)
                return true;
            try
            {
                return _process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public static void Kill()
        {
            var existingPolipo = Process.GetProcessesByName(_exeNameNoExt);
            foreach (var p in existingPolipo)
            {
                string str;
                try
                {
                    str = p.MainModule.FileName;
                }
                catch (Exception)
                {
                    continue;
                }

                if (str == Path.GetFullPath(runningPath + _exeName))
                    try
                    {
                        p.Kill();
                        p.WaitForExit();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
            }
        }

        public void Start(Configuration configuration)
        {
            if (_process == null)
            {
                Kill();
                var polipoConfig = Resources.privoxy_conf;
                RunningPort = GetFreePort();
                polipoConfig = polipoConfig.Replace("__SOCKS_PORT__", configuration.localPort.ToString());
                polipoConfig = polipoConfig.Replace("__PRIVOXY_BIND_PORT__", RunningPort.ToString());
                polipoConfig = polipoConfig.Replace("__PRIVOXY_BIND_IP__", "127.0.0.1");
                polipoConfig = polipoConfig.Replace("__BYPASS_ACTION__", "");
                FileManager.ByteArrayToFile(runningPath + "/privoxy.conf", Encoding.UTF8.GetBytes(polipoConfig));

                Restart();
            }
        }

        public void Restart()
        {
            _process = new Process();
            // Configure the process using the StartInfo properties.
            _process.StartInfo.FileName = runningPath + _exeName;
            _process.StartInfo.Arguments = " \"" + runningPath + "/privoxy.conf\"";
            _process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            _process.StartInfo.UseShellExecute = true;
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.WorkingDirectory = Application.StartupPath;
            //_process.StartInfo.RedirectStandardOutput = true;
            //_process.StartInfo.RedirectStandardError = true;
            try
            {
                _process.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public void Stop()
        {
            if (_process != null)
                try
                {
                    _process.Kill();
                    _process.WaitForExit();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    _process = null;
                }
        }

        private int GetFreePort()
        {
            var defaultPort = 60000;
            try
            {
                var properties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpEndPoints = properties.GetActiveTcpListeners();
                var random = new Random(Utils.GetExecutablePath().GetHashCode() ^ (int) DateTime.Now.Ticks);

                var usedPorts = new List<int>();
                foreach (var endPoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                    usedPorts.Add(endPoint.Port);

                for (var nTry = 0; nTry < 1000; nTry++)
                {
                    var port = random.Next(10000, 65536);
                    if (!usedPorts.Contains(port)) return port;
                }
            }
            catch (Exception e)
            {
                // in case access denied
                Logging.LogUsefulException(e);
                return defaultPort;
            }

            throw new Exception("No free port found.");
        }
    }
}