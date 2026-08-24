using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Satisfying.Shared
{
    public sealed class UdpTransport : ITransport, IDisposable
    {
        readonly Socket _socket;
        readonly bool _isServer;
        readonly byte[] _receiveBuffer = new byte[2048];

        readonly Dictionary<int, IPEndPoint> _peerById = new Dictionary<int, IPEndPoint>();
        readonly Dictionary<string, int> _idByPeer = new Dictionary<string, int>();
        readonly Dictionary<int, double> _peerLastSeen = new Dictionary<int, double>();
        double _now;

        /// <summary>
        /// How long a peer slot may sit unused before a newcomer may take it. An actual player writes
        /// sixty times a second, so anything this stale is a stray datagram, not a person.
        /// </summary>
        const double PeerIdleSeconds = 10.0;

        IPEndPoint _serverEndPoint;
        bool _serverConfirmed;          // client: a handshake has been accepted from _serverEndPoint
        EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

        // Asking the outside world whether this socket is reachable. It has to go out of THIS socket:
        // a probe on any other port answers a question nobody asked.
        ReachabilityProbe _probe;
        readonly Dictionary<string, IPEndPoint> _stunServers = new Dictionary<string, IPEndPoint>();

        public string LastError { get; private set; }
        public int LocalPort { get; private set; }

        /// <summary>Null until hosting starts. See <see cref="BeginReachabilityProbe"/>.</summary>
        public ReachabilityProbe Reachability { get { return _probe; } }

        UdpTransport(Socket socket, bool isServer)
        {
            _socket = socket;
            _isServer = isServer;
            _socket.Blocking = false;
            TryDisableConnectionReset(_socket);
            IPEndPoint local = _socket.LocalEndPoint as IPEndPoint;
            LocalPort = local != null ? local.Port : 0;
        }

        /// <summary>Windows raises a ConnectionReset on the SERVER socket when a client goes away. Silence it.</summary>
        static void TryDisableConnectionReset(Socket socket)
        {
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception)
            {
                // Not supported off Windows - nothing to do.
            }
        }

        public static UdpTransport CreateServer(int port, out string error)
        {
            error = null;
            try
            {
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return new UdpTransport(socket, true);
            }
            catch (Exception e)
            {
                error = "Could not host on port " + port + ": " + e.Message;
                return null;
            }
        }

        public static UdpTransport CreateClient(string host, int port, out string error)
        {
            error = null;
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(host, out address))
                {
                    IPAddress[] resolved = Dns.GetHostAddresses(host);
                    address = null;
                    for (int i = 0; i < resolved.Length; i++)
                    {
                        if (resolved[i].AddressFamily != AddressFamily.InterNetwork) continue;
                        address = resolved[i];
                        break;
                    }
                    if (address == null) { error = "Could not resolve " + host; return null; }
                }

                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                UdpTransport t = new UdpTransport(socket, false);
                t._serverEndPoint = new IPEndPoint(address, port);
                return t;
            }
            catch (Exception e)
            {
                error = "Could not connect to " + host + ":" + port + " - " + e.Message;
                return null;
            }
        }

        public void Update(double now)
        {
            _now = now;
            if (_probe == null) return;

            string host;
            int port;
            byte[] payload;
            if (!_probe.NextRequest(now, out host, out port, out payload)) return;

            IPEndPoint target = ResolveStunServer(host, port);
            if (target == null) return;

            try { _socket.SendTo(payload, 0, payload.Length, SocketFlags.None, target); }
            catch (Exception) { /* the probe retries and eventually reports NoAnswer on its own */ }
        }

        /// <summary>Start asking whether this port is reachable. Server sockets only.</summary>
        public void BeginReachabilityProbe()
        {
            if (!_isServer || _probe != null) return;
            _probe = new ReachabilityProbe(LocalPort);
        }

        /// <summary>Cached: DNS on the frame thread once per server is fine, every retry is not.</summary>
        IPEndPoint ResolveStunServer(string host, int port)
        {
            string key = host + ":" + port;
            IPEndPoint cached;
            if (_stunServers.TryGetValue(key, out cached)) return cached;

            IPEndPoint resolved = null;
            try
            {
                IPAddress[] all = Dns.GetHostAddresses(host);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].AddressFamily != AddressFamily.InterNetwork) continue;
                    resolved = new IPEndPoint(all[i], port);
                    break;
                }
            }
            catch (Exception) { }

            _stunServers[key] = resolved;    // cache the failure too, so a dead name is not retried all game
            return resolved;
        }

        public bool Poll(out int peerId, out byte[] data, out int length)
        {
            peerId = -1;
            data = null;
            length = 0;

            while (true)
            {
                int received;
                EndPoint sender = _from;
                try
                {
                    if (_socket.Available <= 0) return false;
                    received = _socket.ReceiveFrom(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, ref sender);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.WouldBlock) return false;
                    if (e.SocketErrorCode == SocketError.ConnectionReset) continue;   // a peer vanished
                    LastError = e.Message;
                    return false;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    return false;
                }

                if (received <= 0) continue;

                IPEndPoint endPoint = sender as IPEndPoint;
                if (endPoint == null) continue;

                if (_isServer)
                {
                    // Before anything else: a STUN reply is not a player. Letting it fall through to
                    // ResolvePeer would hand a Google server a slot in a two-player game.
                    if (_probe != null &&
                        _probe.HandleDatagram(_receiveBuffer, received, endPoint.Address.ToString())) continue;

                    // Anything from a public address is someone who found us without us writing first,
                    // which is the only proof of reachability that exists.
                    if (_probe != null) _probe.NoteInbound(endPoint.Address.ToString());

                    peerId = ResolvePeer(endPoint);
                    if (peerId < 0) continue;
                }
                else
                {
                    if (_serverEndPoint == null) continue;

                    // Adopt whatever address actually answers. Insisting the reply come from the exact
                    // address we wrote to breaks every case where it legitimately does not: joining a
                    // host on your own LAN through its public address (the router hands the packet over
                    // and the host replies from its private address), a server behind a NAT that
                    // rewrites, a host with several interfaces. We only ever talk to one server, so the
                    // first coherent reply is by definition it - and after this, we write back to the
                    // address that reached us rather than the one that did not.
                    // Before the handshake is accepted we take a reply from anywhere and write back
                    // there, because we cannot know in advance which address will answer. Once the
                    // client has confirmed a real server, only that address is listened to - otherwise
                    // a stray datagram could redirect a live session, or lock the real one out.
                    if (!endPoint.Address.Equals(_serverEndPoint.Address))
                    {
                        if (_serverConfirmed) continue;
                        _serverEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
                    }
                    peerId = 0;
                }

                data = _receiveBuffer;
                length = received;
                return true;
            }
        }

        int ResolvePeer(IPEndPoint endPoint)
        {
            string key = endPoint.Address + ":" + endPoint.Port;
            int id;
            if (_idByPeer.TryGetValue(key, out id))
            {
                _peerLastSeen[id] = _now;
                return id;
            }

            // Lowest free id, never a wrap: reusing a live peer's id would hand them someone else's player.
            id = -1;
            for (int candidate = 1; candidate <= Protocol.MaxPeerId; candidate++)
            {
                if (_peerById.ContainsKey(candidate)) continue;
                id = candidate;
                break;
            }

            // Every source address that ever sent us a byte used to take a slot and keep it forever -
            // a port scan, a stray reply, a client that retried from a new port. Six of those and the
            // table was full, after which every real connection was silently dropped until restart.
            // A slot nobody has used in ten seconds is not in use.
            if (id < 0) id = ReclaimIdlePeer();
            if (id < 0) return -1;

            _idByPeer[key] = id;
            _peerById[id] = new IPEndPoint(endPoint.Address, endPoint.Port);
            _peerLastSeen[id] = _now;
            return id;
        }

        int ReclaimIdlePeer()
        {
            int oldest = -1;
            double oldestSeen = double.MaxValue;
            foreach (KeyValuePair<int, double> kv in _peerLastSeen)
            {
                if (kv.Value >= oldestSeen) continue;
                oldestSeen = kv.Value;
                oldest = kv.Key;
            }
            if (oldest < 0 || _now - oldestSeen < PeerIdleSeconds) return -1;

            Forget(oldest);
            return oldest;
        }

        public void Send(int peerId, byte[] data, int length)
        {
            IPEndPoint target;
            if (_isServer)
            {
                if (!_peerById.TryGetValue(peerId, out target)) return;
            }
            else
            {
                target = _serverEndPoint;
                if (target == null) return;
            }

            try
            {
                _socket.SendTo(data, 0, length, SocketFlags.None, target);
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.WouldBlock) return;
                LastError = e.Message;
            }
            catch (Exception e)
            {
                LastError = e.Message;
            }
        }

        public void ConfirmPeer(int peerId)
        {
            if (!_isServer && peerId == 0) _serverConfirmed = true;
        }

        public void Forget(int peerId)
        {
            IPEndPoint endPoint;
            _peerLastSeen.Remove(peerId);
            if (!_peerById.TryGetValue(peerId, out endPoint)) return;
            _peerById.Remove(peerId);
            _idByPeer.Remove(endPoint.Address + ":" + endPoint.Port);
        }

        public string Describe(int peerId)
        {
            IPEndPoint endPoint;
            if (_isServer && _peerById.TryGetValue(peerId, out endPoint)) return endPoint.ToString();
            return _serverEndPoint != null ? _serverEndPoint.ToString() : "?";
        }

        public void Dispose()
        {
            try { _socket.Close(); }
            catch (Exception) { }
        }

        /// <summary>Best guess at this machine's LAN address, for the "invite a friend" line in the menu.</summary>
        public static string LocalAddress()
        {
            try
            {
                using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect("8.8.8.8", 65530);
                    IPEndPoint local = probe.LocalEndPoint as IPEndPoint;
                    if (local != null) return local.Address.ToString();
                }
            }
            catch (Exception) { }

            try
            {
                IPAddress[] all = Dns.GetHostAddresses(Dns.GetHostName());
                for (int i = 0; i < all.Length; i++)
                    if (all[i].AddressFamily == AddressFamily.InterNetwork) return all[i].ToString();
            }
            catch (Exception) { }

            return "127.0.0.1";
        }
    }
}
