using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Satisfying.Shared
{
    /// <summary>
    /// Asks the router to forward the game port, so hosting from a home connection does not require
    /// anyone to log into a router admin page.
    ///
    /// Two protocols, tried together because routers support one or the other and almost never both:
    ///
    ///   UPnP IGD - multicast an SSDP search, fetch the device description, find the WAN connection
    ///              service, POST an AddPortMapping SOAP call.
    ///   NAT-PMP  - a five byte datagram to the gateway on port 5351. Apple's answer, and what most
    ///              consumer routers that refuse UPnP still speak.
    ///
    /// All of it runs on a worker thread with hard timeouts and every failure swallowed into
    /// <see cref="Status"/>: a router that ignores us must cost a few seconds and nothing else. The
    /// mapping is removed on shutdown, and it carries a lease so it expires on its own if we crash.
    /// </summary>
    public sealed class PortMapper : IDisposable
    {
        public enum Result { Idle, Working, Mapped, Failed }

        public Result State { get; private set; }
        public string Status { get; private set; }
        public string ExternalAddress { get; private set; }
        public string Method { get; private set; }

        const int LeaseSeconds = 7200;
        const int SsdpPort = 1900;
        const int NatPmpPort = 5351;
        static readonly IPAddress SsdpMulticast = IPAddress.Parse("239.255.255.250");

        readonly int _port;
        readonly string _localAddress;
        Thread _worker;
        volatile bool _stop;

        // Filled in by whichever protocol succeeded, so the mapping can be taken down again.
        string _controlUrl;
        string _serviceType;
        volatile bool _mappedUpnp;
        volatile bool _mappedPmp;
        IPAddress _gateway;

        public PortMapper(int port, string localAddress)
        {
            _port = port;
            _localAddress = localAddress;
            State = Result.Idle;
            Status = "not started";
        }

        public void Begin()
        {
            if (_worker != null) return;
            State = Result.Working;
            Status = "asking the router to forward " + _port + "...";
            _worker = new Thread(Run);
            _worker.IsBackground = true;
            _worker.Start();
        }

        void Run()
        {
            try
            {
                if (TryUpnp()) { Method = "UPnP"; Finish(true, "router is forwarding UDP " + _port + " (UPnP)"); return; }
                if (_stop) return;
                if (TryNatPmp()) { Method = "NAT-PMP"; Finish(true, "router is forwarding UDP " + _port + " (NAT-PMP)"); return; }
                Finish(false, "the router did not answer - forward UDP " + _port + " by hand to play over the internet");
            }
            catch (Exception e)
            {
                Finish(false, "port forwarding failed: " + e.Message);
            }
        }

        void Finish(bool ok, string message)
        {
            Status = message;
            State = ok ? Result.Mapped : Result.Failed;
        }

        // ================================================================== UPnP
        bool TryUpnp()
        {
            string description = DiscoverUpnp();
            if (description == null) return false;

            string xml = HttpGet(description, 3000);
            if (xml == null) return false;

            string baseUrl = description;
            if (!FindService(xml, out _serviceType, out _controlUrl)) return false;
            _controlUrl = AbsoluteUrl(baseUrl, _controlUrl);

            string body =
                "<u:AddPortMapping xmlns:u=\"" + _serviceType + "\">" +
                "<NewRemoteHost></NewRemoteHost>" +
                "<NewExternalPort>" + _port + "</NewExternalPort>" +
                "<NewProtocol>UDP</NewProtocol>" +
                "<NewInternalPort>" + _port + "</NewInternalPort>" +
                "<NewInternalClient>" + _localAddress + "</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled>" +
                "<NewPortMappingDescription>Satisfying</NewPortMappingDescription>" +
                "<NewLeaseDuration>" + LeaseSeconds + "</NewLeaseDuration>" +
                "</u:AddPortMapping>";

            if (Soap(_controlUrl, _serviceType, "AddPortMapping", body) == null) return false;
            _mappedUpnp = true;

            string external = Soap(_controlUrl, _serviceType,
                "GetExternalIPAddress",
                "<u:GetExternalIPAddress xmlns:u=\"" + _serviceType + "\"></u:GetExternalIPAddress>");
            if (external != null) ExternalAddress = Between(external, "<NewExternalIPAddress>", "</NewExternalIPAddress>");
            return true;
        }

        /// <summary>SSDP: shout on the multicast group and take the first gateway that answers.</summary>
        string DiscoverUpnp()
        {
            string request =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: 239.255.255.250:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 2\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

            using (UdpClient udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = 1200;
                byte[] payload = Encoding.ASCII.GetBytes(request);
                IPEndPoint target = new IPEndPoint(SsdpMulticast, SsdpPort);

                for (int attempt = 0; attempt < 3 && !_stop; attempt++)
                {
                    try { udp.Send(payload, payload.Length, target); }
                    catch (SocketException) { return null; }

                    try
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] reply = udp.Receive(ref from);
                        string text = Encoding.ASCII.GetString(reply);
                        string location = HeaderValue(text, "LOCATION");
                        if (!string.IsNullOrEmpty(location)) return location;
                    }
                    catch (SocketException) { /* nobody answered this round */ }
                }
            }
            return null;
        }

        /// <summary>
        /// The description lists several services; we want whichever WAN connection service is there.
        /// IPConnection is the common one, PPPConnection turns up on DSL gear.
        /// </summary>
        public static bool FindService(string xml, out string serviceType, out string controlUrl)
        {
            string[] wanted =
            {
                "urn:schemas-upnp-org:service:WANIPConnection:2",
                "urn:schemas-upnp-org:service:WANIPConnection:1",
                "urn:schemas-upnp-org:service:WANPPPConnection:1"
            };

            for (int i = 0; i < wanted.Length; i++)
            {
                int at = xml.IndexOf(wanted[i], StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                int control = xml.IndexOf("<controlURL>", at, StringComparison.OrdinalIgnoreCase);
                if (control < 0) continue;
                int end = xml.IndexOf("</controlURL>", control, StringComparison.OrdinalIgnoreCase);
                if (end < 0) continue;
                serviceType = wanted[i];
                controlUrl = xml.Substring(control + 12, end - control - 12).Trim();
                return true;
            }
            serviceType = null;
            controlUrl = null;
            return false;
        }

        string Soap(string url, string serviceType, string action, string body)
        {
            string envelope =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                body + "</s:Body></s:Envelope>";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "text/xml; charset=\"utf-8\"";
                request.Headers.Add("SOAPACTION", "\"" + serviceType + "#" + action + "\"");
                request.Timeout = 4000;
                request.ReadWriteTimeout = 4000;

                byte[] data = Encoding.UTF8.GetBytes(envelope);
                request.ContentLength = data.Length;
                using (System.IO.Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string HttpGet(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ================================================================== NAT-PMP
        /// <summary>
        /// Five bytes to the gateway: version 0, opcode 2 (map UDP), the port twice, a lease. The
        /// reply carries the external port it actually gave us, which is usually the one we asked for.
        /// </summary>
        bool TryNatPmp()
        {
            _gateway = DefaultGateway();
            if (_gateway == null) return false;

            byte[] request = BuildNatPmpRequest(_port, LeaseSeconds);

            using (UdpClient udp = new UdpClient())
            {
                udp.Client.ReceiveTimeout = 900;
                IPEndPoint target = new IPEndPoint(_gateway, NatPmpPort);
                for (int attempt = 0; attempt < 3 && !_stop; attempt++)
                {
                    try { udp.Send(request, request.Length, target); }
                    catch (SocketException) { return false; }

                    try
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] reply = udp.Receive(ref from);
                        if (reply.Length < 16) continue;
                        if (reply[0] != 0 || reply[1] != 129) continue;          // 128 + opcode
                        int code = (reply[2] << 8) | reply[3];
                        if (code != 0) return false;
                        _mappedPmp = true;
                        return true;
                    }
                    catch (SocketException) { /* try again */ }
                }
            }
            return false;
        }

        /// <summary>
        /// The gateway of whichever interface actually carries traffic. Taken from the routing table
        /// via NetworkInterface rather than guessed at from the subnet.
        /// </summary>
        static IPAddress DefaultGateway()
        {
            try
            {
                System.Net.NetworkInformation.NetworkInterface[] interfaces =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

                for (int i = 0; i < interfaces.Length; i++)
                {
                    System.Net.NetworkInformation.NetworkInterface nic = interfaces[i];
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    foreach (System.Net.NetworkInformation.GatewayIPAddressInformation gateway
                             in nic.GetIPProperties().GatewayAddresses)
                    {
                        if (gateway.Address == null) continue;
                        if (gateway.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (gateway.Address.Equals(IPAddress.Any)) continue;
                        return gateway.Address;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        // ================================================================== teardown
        public void Dispose()
        {
            _stop = true;
            try
            {
                if (_mappedUpnp && _controlUrl != null)
                {
                    Soap(_controlUrl, _serviceType, "DeletePortMapping",
                        "<u:DeletePortMapping xmlns:u=\"" + _serviceType + "\">" +
                        "<NewRemoteHost></NewRemoteHost>" +
                        "<NewExternalPort>" + _port + "</NewExternalPort>" +
                        "<NewProtocol>UDP</NewProtocol>" +
                        "</u:DeletePortMapping>");
                    _mappedUpnp = false;
                }

                if (_mappedPmp && _gateway != null)
                {
                    // Same call with a zero lease is how NAT-PMP says "remove it".
                    byte[] request = BuildNatPmpRequest(_port, 0);
                    using (UdpClient udp = new UdpClient())
                        udp.Send(request, request.Length, new IPEndPoint(_gateway, NatPmpPort));
                    _mappedPmp = false;
                }
            }
            catch (Exception) { }

            if (_worker != null && _worker.IsAlive) _worker.Join(200);
            _worker = null;
        }

        // ================================================================== small helpers
        /// <summary>The NAT-PMP map request, exposed so its layout can be asserted against the RFC.</summary>
        public static byte[] BuildNatPmpRequest(int port, int leaseSeconds)
        {
            byte[] request = new byte[12];
            request[0] = 0;                     // version
            request[1] = 1;                     // opcode 1 = map UDP
            WriteUInt16(request, 4, (ushort)port);
            WriteUInt16(request, 6, (ushort)(leaseSeconds > 0 ? port : 0));
            WriteUInt32(request, 8, (uint)(leaseSeconds > 0 ? leaseSeconds : 0));
            return request;
        }

        static void WriteUInt16(byte[] b, int at, ushort value)
        {
            b[at] = (byte)(value >> 8);
            b[at + 1] = (byte)(value & 0xFF);
        }

        static void WriteUInt32(byte[] b, int at, uint value)
        {
            b[at] = (byte)(value >> 24);
            b[at + 1] = (byte)((value >> 16) & 0xFF);
            b[at + 2] = (byte)((value >> 8) & 0xFF);
            b[at + 3] = (byte)(value & 0xFF);
        }

        public static string HeaderValue(string response, string header)
        {
            string[] lines = response.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length <= header.Length) continue;
                if (!line.StartsWith(header, StringComparison.OrdinalIgnoreCase)) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        public static string Between(string text, string open, string close)
        {
            int a = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (a < 0) return null;
            a += open.Length;
            int b = text.IndexOf(close, a, StringComparison.OrdinalIgnoreCase);
            return b < 0 ? null : text.Substring(a, b - a).Trim();
        }

        /// <summary>Control URLs come back relative more often than not.</summary>
        public static string AbsoluteUrl(string baseUrl, string path)
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;

            try
            {
                Uri root = new Uri(baseUrl);
                return new Uri(root, path).ToString();
            }
            catch (Exception)
            {
                return path;
            }
        }

        public static string Describe(Result state)
        {
            switch (state)
            {
                case Result.Working: return "opening the port...";
                case Result.Mapped: return "port open";
                case Result.Failed: return "port not opened";
                default: return "";
            }
        }

        public override string ToString()
        {
            return Method + " " + State.ToString().ToLower(CultureInfo.InvariantCulture) + ": " + Status;
        }

        /// <summary>Every local IPv4 address, so the host can be told which one to hand out.</summary>
        public static List<string> LocalAddresses()
        {
            List<string> found = new List<string>();
            try
            {
                IPHostEntry entry = Dns.GetHostEntry(Dns.GetHostName());
                for (int i = 0; i < entry.AddressList.Length; i++)
                {
                    IPAddress address = entry.AddressList[i];
                    if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(address)) continue;
                    found.Add(address.ToString());
                }
            }
            catch (Exception) { }
            return found;
        }
    }
}
