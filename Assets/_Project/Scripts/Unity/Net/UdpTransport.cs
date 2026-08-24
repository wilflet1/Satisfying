using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{

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

