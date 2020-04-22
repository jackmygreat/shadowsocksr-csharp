using System.Net;
using System.Net.Sockets;

namespace Shadowsocks.Util
{
    public static class ServerName
    {
        public static string HideServerAddr(string addr)
        {
            IPAddress ipAddr;
            var serverAlterName = addr;

            var parsed = IPAddress.TryParse(addr, out ipAddr);
            if (parsed)
            {
                char separator;
                if (AddressFamily.InterNetwork == ipAddr.AddressFamily)
                    separator = '.'; // IPv4
                else
                    separator = ':'; // IPv6

                serverAlterName = HideAddr(addr, separator);
            }
            else
            {
                var pos = addr.IndexOf('.', 1);
                if (pos > 0) serverAlterName = "*" + addr.Substring(pos);
            }

            return serverAlterName;
        }

        private static string HideAddr(string addr, char separator)
        {
            var result = "";

            var splited = addr.Split(separator);
            var prefix = splited[0];
            var suffix = splited[splited.Length - 1];

            if (0 < prefix.Length)
                result = prefix + separator;

            result += "**";

            if (0 < suffix.Length)
                result += separator + suffix;

            return result;
        }
    }
}