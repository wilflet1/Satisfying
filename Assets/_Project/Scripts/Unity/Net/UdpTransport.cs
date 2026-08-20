using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Plain UDP. One socket, non blocking, drained once per frame on the main thread - no worker
    /// threads means no locks and no race conditions in the netcode above it.
    /// Peer 0 always means "the server"; the server hands every remote endpoint an id of its own.
    /// </summary>
    public sealed class UdpTransport : ITransport, IDisposable
    {
        readonly Socket _socket;
        readonly bool _isServer;
        readonly byte[] _receiveBuffer = new byte[2048];

        readonly Dictionary<int, IPEndPoint> _peerById = new Dictionary<int, IPEndPoint>();
        readonly Dictionary<string, int> _idByPeer = new Dictionary<string, int>();

        IPEndPoint _serverEndPoint;
        EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

        public string LastError { get; private set; }
        public int LocalPort { get; private set; }

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

        public void Update(double now) { }

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
                    peerId = ResolvePeer(endPoint);
                    if (peerId < 0) continue;
                }
                else
                {
                    if (_serverEndPoint == null || !endPoint.Address.Equals(_serverEndPoint.Address)) continue;
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
            if (_idByPeer.TryGetValue(key, out id)) return id;

            // Lowest free id, never a wrap: reusing a live peer's id would hand them someone else's player.
            id = -1;
            for (int candidate = 1; candidate <= Protocol.MaxPlayers; candidate++)
            {
                if (_peerById.ContainsKey(candidate)) continue;
                id = candidate;
                break;
            }
            if (id < 0) return -1;      // server full; the connect request is simply dropped

            _idByPeer[key] = id;
            _peerById[id] = new IPEndPoint(endPoint.Address, endPoint.Port);
            return id;
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

        public void Forget(int peerId)
        {
            IPEndPoint endPoint;
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

    /// <summary>
    /// A one-line LAN beacon so a friend on the same network can click "join" instead of typing an IP.
    /// </summary>
    public sealed class LanDiscovery : IDisposable
    {
        public const int BeaconPort = 7778;
        const string Magic = "SATISFYING1";

        public struct Found
        {
            public string Name;
            public string Address;
            public int Port;
            public double SeenAt;
        }

        readonly Socket _socket;
        readonly byte[] _buffer = new byte[512];
        EndPoint _from = new IPEndPoint(IPAddress.Any, 0);
        readonly List<Found> _found = new List<Found>();
        double _lastBeacon;

        public bool Listening { get; private set; }
        public IReadOnlyList<Found> Servers { get { return _found; } }

        public LanDiscovery(bool listen)
        {
            Listening = listen;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.EnableBroadcast = true;
            _socket.Blocking = false;
            try
            {
                _socket.Bind(new IPEndPoint(IPAddress.Any, listen ? BeaconPort : 0));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[lan] could not bind discovery socket: " + e.Message);
            }
        }

        /// <summary>Host side: shout our name and port on the local network once a second.</summary>
        public void Broadcast(double now, string serverName, int port)
        {
            if (now - _lastBeacon < 1.0) return;
            _lastBeacon = now;
            try
            {
                byte[] payload = System.Text.Encoding.UTF8.GetBytes(Magic + "|" + serverName + "|" + port);
                _socket.SendTo(payload, new IPEndPoint(IPAddress.Broadcast, BeaconPort));
            }
            catch (Exception) { }
        }

        /// <summary>Client side: collect beacons and expire anything we have not heard from in 5 seconds.</summary>
        public void Poll(double now)
        {
            if (!Listening) return;

            while (true)
            {
                int received;
                EndPoint sender = _from;
                try
                {
                    if (_socket.Available <= 0) break;
                    received = _socket.ReceiveFrom(_buffer, ref sender);
                }
                catch (Exception) { break; }
                if (received <= 0) continue;

                string text = System.Text.Encoding.UTF8.GetString(_buffer, 0, received);
                string[] parts = text.Split('|');
                if (parts.Length != 3 || parts[0] != Magic) continue;

                IPEndPoint endPoint = sender as IPEndPoint;
                if (endPoint == null) continue;

                int port;
                if (!int.TryParse(parts[2], out port)) continue;

                string address = endPoint.Address.ToString();
                bool updated = false;
                for (int i = 0; i < _found.Count; i++)
                {
                    if (_found[i].Address != address || _found[i].Port != port) continue;
                    Found f = _found[i];
                    f.SeenAt = now;
                    f.Name = parts[1];
                    _found[i] = f;
                    updated = true;
                    break;
                }

                if (!updated)
                {
                    Found f;
                    f.Name = parts[1];
                    f.Address = address;
                    f.Port = port;
                    f.SeenAt = now;
                    _found.Add(f);
                }
            }

            for (int i = _found.Count - 1; i >= 0; i--)
                if (now - _found[i].SeenAt > 5.0) _found.RemoveAt(i);
        }

        public void Dispose()
        {
            try { _socket.Close(); }
            catch (Exception) { }
        }
    }
}
