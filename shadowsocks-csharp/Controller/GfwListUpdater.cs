using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Shadowsocks.Model;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public class GFWListUpdater
    {
        private const string GFWLIST_URL = "https://raw.githubusercontent.com/gfwlist/gfwlist/master/gfwlist.txt";

        private const string GFWLIST_BACKUP_URL =
            "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/gfwlist.txt";

        private const string GFWLIST_TEMPLATE_URL =
            "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_gfw.pac";

        private static readonly string PAC_FILE = PACServer.PAC_FILE;

        private static readonly string USER_RULE_FILE = PACServer.USER_RULE_FILE;

        private static readonly string USER_ABP_FILE = PACServer.USER_ABP_FILE;

        private static string gfwlist_template;

        private Configuration lastConfig;

        public int update_type;

        public event EventHandler<ResultEventArgs> UpdateCompleted;

        public event ErrorEventHandler Error;

        private void http_DownloadGFWTemplateCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                var result = e.Result;
                if (result.IndexOf("__RULES__") > 0 && result.IndexOf("FindProxyForURL") > 0)
                {
                    gfwlist_template = result;
                    if (lastConfig != null) UpdatePACFromGFWList(lastConfig);
                    lastConfig = null;
                }
                else
                {
                    Error(this, new ErrorEventArgs(new Exception("Download ERROR")));
                }
            }
            catch (Exception ex)
            {
                if (Error != null) Error(this, new ErrorEventArgs(ex));
            }
        }

        private void http_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                var lines = ParseResult(e.Result);
                if (lines.Count == 0) throw new Exception("Empty GFWList");
                if (File.Exists(USER_RULE_FILE))
                {
                    var local = File.ReadAllText(USER_RULE_FILE, Encoding.UTF8);
                    var rules = local.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rule in rules)
                    {
                        if (rule.StartsWith("!") || rule.StartsWith("["))
                            continue;
                        lines.Add(rule);
                    }
                }

                var abpContent = gfwlist_template;
                if (File.Exists(USER_ABP_FILE))
                    abpContent = File.ReadAllText(USER_ABP_FILE, Encoding.UTF8);
                else
                    abpContent = gfwlist_template;
                abpContent = abpContent.Replace("__RULES__", SimpleJson.SimpleJson.SerializeObject(lines));
                if (File.Exists(PAC_FILE))
                {
                    var original = File.ReadAllText(PAC_FILE, Encoding.UTF8);
                    if (original == abpContent)
                    {
                        update_type = 0;
                        UpdateCompleted(this, new ResultEventArgs(false));
                        return;
                    }
                }

                File.WriteAllText(PAC_FILE, abpContent, Encoding.UTF8);
                if (UpdateCompleted != null)
                {
                    update_type = 0;
                    UpdateCompleted(this, new ResultEventArgs(true));
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    var http = sender as WebClient;
                    if (http.BaseAddress.StartsWith(GFWLIST_URL))
                    {
                        http.BaseAddress = GFWLIST_BACKUP_URL;
                        http.DownloadStringAsync(new Uri(GFWLIST_BACKUP_URL + "?rnd=" + Utils.RandUInt32()));
                    }
                    else
                    {
                        if (e.Error != null)
                            Error(this, new ErrorEventArgs(e.Error));
                        else
                            Error(this, new ErrorEventArgs(ex));
                    }
                }
            }
        }

        private void http_DownloadPACCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                var content = e.Result;
                if (File.Exists(PAC_FILE))
                {
                    var original = File.ReadAllText(PAC_FILE, Encoding.UTF8);
                    if (original == content)
                    {
                        update_type = 1;
                        UpdateCompleted(this, new ResultEventArgs(false));
                        return;
                    }
                }

                File.WriteAllText(PAC_FILE, content, Encoding.UTF8);
                if (UpdateCompleted != null)
                {
                    update_type = 1;
                    UpdateCompleted(this, new ResultEventArgs(true));
                }
            }
            catch (Exception ex)
            {
                if (Error != null) Error(this, new ErrorEventArgs(ex));
            }
        }

        public void UpdatePACFromGFWList(Configuration config)
        {
            if (gfwlist_template == null)
            {
                lastConfig = config;
                var http = new WebClient();
                http.Headers.Add("User-Agent",
                    string.IsNullOrEmpty(config.proxyUserAgent)
                        ? "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                        : config.proxyUserAgent);
                var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                if (!string.IsNullOrEmpty(config.authPass))
                    proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
                http.Proxy = proxy;
                http.DownloadStringCompleted += http_DownloadGFWTemplateCompleted;
                http.DownloadStringAsync(new Uri(GFWLIST_TEMPLATE_URL + "?rnd=" + Utils.RandUInt32()));
            }
            else
            {
                var http = new WebClient();
                http.Headers.Add("User-Agent",
                    string.IsNullOrEmpty(config.proxyUserAgent)
                        ? "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                        : config.proxyUserAgent);
                var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                if (!string.IsNullOrEmpty(config.authPass))
                    proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
                http.Proxy = proxy;
                http.BaseAddress = GFWLIST_URL;
                http.DownloadStringCompleted += http_DownloadStringCompleted;
                http.DownloadStringAsync(new Uri(GFWLIST_URL + "?rnd=" + Utils.RandUInt32()));
            }
        }

        public void UpdatePACFromGFWList(Configuration config, string url)
        {
            var http = new WebClient();
            http.Headers.Add("User-Agent",
                string.IsNullOrEmpty(config.proxyUserAgent)
                    ? "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);
            var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
            if (!string.IsNullOrEmpty(config.authPass))
                proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
            http.Proxy = proxy;
            http.DownloadStringCompleted += http_DownloadPACCompleted;
            http.DownloadStringAsync(new Uri(url + "?rnd=" + Utils.RandUInt32()));
        }

        public List<string> ParseResult(string response)
        {
            var bytes = Convert.FromBase64String(response);
            var content = Encoding.ASCII.GetString(bytes);
            var lines = content.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            var valid_lines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (line.StartsWith("!") || line.StartsWith("["))
                    continue;
                valid_lines.Add(line);
            }

            return valid_lines;
        }

        public class ResultEventArgs : EventArgs
        {
            public bool Success;

            public ResultEventArgs(bool success)
            {
                Success = success;
            }
        }
    }
}