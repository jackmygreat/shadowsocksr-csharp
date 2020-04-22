using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Shadowsocks.Controller;
using Shadowsocks.Util;
#if !_CONSOLE
#endif

namespace Shadowsocks.Model
{
    public class DnsBuffer
    {
        public bool force_expired;
        public string host;
        public IPAddress ip;
        public DateTime updateTime;

        public bool isExpired(string host)
        {
            if (updateTime == null) return true;
            if (this.host != host) return true;
            if (force_expired && (DateTime.Now - updateTime).TotalMinutes > 1) return true;
            return (DateTime.Now - updateTime).TotalMinutes > 30;
        }

        public void UpdateDns(string host, IPAddress ip)
        {
            updateTime = DateTime.Now;
            this.ip = new IPAddress(ip.GetAddressBytes());
            this.host = host;
            force_expired = false;
        }
    }

    public class Connections
    {
        private readonly Dictionary<IHandler, int> sockets = new Dictionary<IHandler, int>();

        public int Count => sockets.Count;

        public bool AddRef(IHandler socket)
        {
            lock (this)
            {
                if (sockets.ContainsKey(socket))
                    sockets[socket] += 1;
                else
                    sockets[socket] = 1;
                return true;
            }
        }

        public bool DecRef(IHandler socket)
        {
            lock (this)
            {
                if (sockets.ContainsKey(socket))
                {
                    sockets[socket] -= 1;
                    if (sockets[socket] == 0) sockets.Remove(socket);
                }
                else
                {
                    return false;
                }

                return true;
            }
        }

        public void CloseAll()
        {
            IHandler[] s;
            lock (this)
            {
                s = new IHandler[sockets.Count];
                sockets.Keys.CopyTo(s, 0);
            }

            foreach (var handler in s)
                try
                {
                    handler.Shutdown();
                }
                catch
                {
                }
        }
    }

    [Serializable]
    public class Server
    {
        private static Server forwardServer = new Server();
        private Connections Connections = new Connections();
        private DnsBuffer dnsBuffer = new DnsBuffer();
        public bool enable;
        public string group;
        public string id;
        public string method;
        public string obfs;
        private object obfsdata;
        public string obfsparam;
        public string password;
        public string protocol;

        private object protocoldata;
        public string protocolparam;
        public string remarks_base64;
        public string server;
        public ushort server_port;
        public ushort server_udp_port;
        private ServerSpeedLog serverSpeedLog = new ServerSpeedLog();
        public bool udp_over_tcp;

        public Server()
        {
            server = "server host";
            server_port = 8388;
            method = "aes-256-cfb";
            protocol = "origin";
            protocolparam = "";
            obfs = "plain";
            obfsparam = "";
            password = "0";
            remarks_base64 = "";
            group = "FreeSSR-public";
            udp_over_tcp = false;
            enable = true;
            var id = new byte[16];
            Utils.RandBytes(id, id.Length);
            this.id = BitConverter.ToString(id).Replace("-", "");
        }

        public Server(string ssURL, string force_group) : this()
        {
            if (ssURL.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                ServerFromSS(ssURL, force_group);
            else if (ssURL.StartsWith("ssr://", StringComparison.OrdinalIgnoreCase))
                ServerFromSSR(ssURL, force_group);
            else
                throw new FormatException();
        }

        public string remarks
        {
            get
            {
                if (remarks_base64.Length == 0) return string.Empty;
                try
                {
                    return Base64.DecodeUrlSafeBase64(remarks_base64);
                }
                catch (FormatException)
                {
                    var old = remarks_base64;
                    remarks = remarks_base64;
                    return old;
                }
            }
            set => remarks_base64 = Base64.EncodeUrlSafeBase64(value);
        }

        public void CopyServer(Server Server)
        {
            protocoldata = Server.protocoldata;
            obfsdata = Server.obfsdata;
            serverSpeedLog = Server.serverSpeedLog;
            dnsBuffer = Server.dnsBuffer;
            Connections = Server.Connections;
            enable = Server.enable;
        }

        public void CopyServerInfo(Server Server)
        {
            remarks = Server.remarks;
            group = Server.group;
        }

        public static Server GetForwardServerRef()
        {
            return forwardServer;
        }

        public void SetConnections(Connections Connections)
        {
            this.Connections = Connections;
        }

        public Connections GetConnections()
        {
            return Connections;
        }

        public DnsBuffer DnsBuffer()
        {
            return dnsBuffer;
        }

        public ServerSpeedLog ServerSpeedLog()
        {
            return serverSpeedLog;
        }

        public void SetServerSpeedLog(ServerSpeedLog log)
        {
            serverSpeedLog = log;
        }

        public string FriendlyName()
        {
            if (string.IsNullOrEmpty(server)) return I18N.GetString("New server");
            if (string.IsNullOrEmpty(remarks_base64))
            {
                if (server.IndexOf(':') >= 0)
                    return "[" + server + "]:" + server_port;
                return server + ":" + server_port;
            }

            if (server.IndexOf(':') >= 0)
                return remarks + " ([" + server + "]:" + server_port + ")";
            return remarks + " (" + server + ":" + server_port + ")";
        }

        public string HiddenName(bool hide = true)
        {
            if (string.IsNullOrEmpty(server)) return I18N.GetString("New server");
            var server_alter_name = server;
            if (hide) server_alter_name = ServerName.HideServerAddr(server);
            if (string.IsNullOrEmpty(remarks_base64))
            {
                if (server.IndexOf(':') >= 0)
                    return "[" + server_alter_name + "]:" + server_port;
                return server_alter_name + ":" + server_port;
            }

            if (server.IndexOf(':') >= 0)
                return remarks + " ([" + server_alter_name + "]:" + server_port + ")";
            return remarks + " (" + server_alter_name + ":" + server_port + ")";
        }

        public Server Clone()
        {
            var ret = new Server();
            ret.server = server;
            ret.server_port = server_port;
            ret.password = password;
            ret.method = method;
            ret.protocol = protocol;
            ret.obfs = obfs;
            ret.obfsparam = obfsparam ?? "";
            ret.remarks_base64 = remarks_base64;
            ret.group = group;
            ret.enable = enable;
            ret.udp_over_tcp = udp_over_tcp;
            ret.id = id;
            ret.protocoldata = protocoldata;
            ret.obfsdata = obfsdata;
            return ret;
        }

        public bool isMatchServer(Server server)
        {
            if (this.server == server.server
                && server_port == server.server_port
                && server_udp_port == server.server_udp_port
                && method == server.method
                && protocol == server.protocol
                && protocolparam == server.protocolparam
                && obfs == server.obfs
                && obfsparam == server.obfsparam
                && password == server.password
                && udp_over_tcp == server.udp_over_tcp
            )
                return true;
            return false;
        }

        private Dictionary<string, string> ParseParam(string param_str)
        {
            var params_dict = new Dictionary<string, string>();
            var obfs_params = param_str.Split('&');
            foreach (var p in obfs_params)
                if (p.IndexOf('=') > 0)
                {
                    var index = p.IndexOf('=');
                    string key, val;
                    key = p.Substring(0, index);
                    val = p.Substring(index + 1);
                    params_dict[key] = val;
                }

            return params_dict;
        }

        public void ServerFromSSR(string ssrURL, string force_group)
        {
            // ssr://host:port:protocol:method:obfs:base64pass/?obfsparam=base64&remarks=base64&group=base64&udpport=0&uot=1
            var ssr = Regex.Match(ssrURL, "ssr://([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
            if (!ssr.Success)
                throw new FormatException();

            var data = Base64.DecodeUrlSafeBase64(ssr.Groups[1].Value);
            var params_dict = new Dictionary<string, string>();

            Match match = null;

            var param_start_pos = data.IndexOf("?");
            if (param_start_pos > 0)
            {
                params_dict = ParseParam(data.Substring(param_start_pos + 1));
                data = data.Substring(0, param_start_pos);
            }

            if (data.IndexOf("/") >= 0) data = data.Substring(0, data.LastIndexOf("/"));

            var UrlFinder = new Regex("^(.+):([^:]+):([^:]*):([^:]+):([^:]*):([^:]+)");
            match = UrlFinder.Match(data);

            if (match == null || !match.Success)
                throw new FormatException();

            server = match.Groups[1].Value;
            server_port = ushort.Parse(match.Groups[2].Value);
            protocol = match.Groups[3].Value.Length == 0 ? "origin" : match.Groups[3].Value;
            protocol = protocol.Replace("_compatible", "");
            method = match.Groups[4].Value;
            obfs = match.Groups[5].Value.Length == 0 ? "plain" : match.Groups[5].Value;
            obfs = obfs.Replace("_compatible", "");
            password = Base64.DecodeStandardSSRUrlSafeBase64(match.Groups[6].Value);

            if (params_dict.ContainsKey("protoparam"))
                protocolparam = Base64.DecodeStandardSSRUrlSafeBase64(params_dict["protoparam"]);
            if (params_dict.ContainsKey("obfsparam"))
                obfsparam = Base64.DecodeStandardSSRUrlSafeBase64(params_dict["obfsparam"]);
            if (params_dict.ContainsKey("remarks"))
                remarks = Base64.DecodeStandardSSRUrlSafeBase64(params_dict["remarks"]);
            if (params_dict.ContainsKey("group"))
                group = Base64.DecodeStandardSSRUrlSafeBase64(params_dict["group"]);
            else
                group = "";
            if (params_dict.ContainsKey("uot")) udp_over_tcp = int.Parse(params_dict["uot"]) != 0;
            if (params_dict.ContainsKey("udpport")) server_udp_port = ushort.Parse(params_dict["udpport"]);
            if (!string.IsNullOrEmpty(force_group))
                group = force_group;
        }

        public void ServerFromSS(string ssURL, string force_group)
        {
            Regex UrlFinder = new Regex("^(?i)ss://([A-Za-z0-9+-/=_]+)(#(.+))?", RegexOptions.IgnoreCase),
                DetailsParser = new Regex("^((?<method>.+):(?<password>.*)@(?<hostname>.+?)" +
                                          ":(?<port>\\d+?))$", RegexOptions.IgnoreCase);

            var match = UrlFinder.Match(ssURL);
            if (!match.Success)
                throw new FormatException();

            var base64 = match.Groups[1].Value;
            match = DetailsParser.Match(Encoding.UTF8.GetString(Convert.FromBase64String(
                base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='))));
            protocol = "origin";
            method = match.Groups["method"].Value;
            password = match.Groups["password"].Value;
            server = match.Groups["hostname"].Value;
            server_port = ushort.Parse(match.Groups["port"].Value);
            if (!string.IsNullOrEmpty(force_group))
                group = force_group;
            else
                group = "";
        }

        public string GetSSLinkForServer()
        {
            var parts = method + ":" + password + "@" + server + ":" + server_port;
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(parts)).Replace("=", "");
            return "ss://" + base64;
        }

        public string GetSSRLinkForServer()
        {
            var main_part = server + ":" + server_port + ":" + protocol + ":" + method + ":" + obfs + ":" +
                            Base64.EncodeUrlSafeBase64(password);
            var param_str = "obfsparam=" + Base64.EncodeUrlSafeBase64(obfsparam ?? "");
            if (!string.IsNullOrEmpty(protocolparam))
                param_str += "&protoparam=" + Base64.EncodeUrlSafeBase64(protocolparam);
            if (!string.IsNullOrEmpty(remarks)) param_str += "&remarks=" + Base64.EncodeUrlSafeBase64(remarks);
            if (!string.IsNullOrEmpty(group)) param_str += "&group=" + Base64.EncodeUrlSafeBase64(group);
            if (udp_over_tcp) param_str += "&uot=" + "1";
            if (server_udp_port > 0) param_str += "&udpport=" + server_udp_port;
            var base64 = Base64.EncodeUrlSafeBase64(main_part + "/?" + param_str);
            return "ssr://" + base64;
        }

        public bool isEnable()
        {
            return enable;
        }

        public void setEnable(bool enable)
        {
            this.enable = enable;
        }

        public object getObfsData()
        {
            return obfsdata;
        }

        public void setObfsData(object data)
        {
            obfsdata = data;
        }

        public object getProtocolData()
        {
            return protocoldata;
        }

        public void setProtocolData(object data)
        {
            protocoldata = data;
        }
    }
}