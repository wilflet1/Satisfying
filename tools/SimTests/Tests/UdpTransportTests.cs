using System.Net;
using System.Net.Sockets;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// Real sockets on loopback. Everything else in this harness runs over an in-process link, which
    /// means UdpTransport - the one piece that only ever runs against a real network - had no coverage
    /// at all, and the bugs that hid there were exactly the ones that never show up on localhost.
    /// </summary>
    public static class UdpTransportTests
    {
        static int FreePort()
        {
            using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)probe.LocalEndPoint).Port;
            }
        }

        static void Pump(UdpTransport a, UdpTransport b, double now)
        {
            a.Update(now);
            b.Update(now);
        }

        public static void Register()
        {
            TestRunner.Add("udp/a datagram makes the round trip and the peer keeps its id", () =>
            {
                string error;
                int port = FreePort();
                UdpTransport server = UdpTransport.CreateServer(port, out error);
                Assert.True(server != null, "server bound: " + error);
                UdpTransport client = UdpTransport.CreateClient("127.0.0.1", port, out error);
                Assert.True(client != null, "client made: " + error);

                client.Send(0, new byte[] { 1, 2, 3 }, 3);

                int peerId; byte[] data; int length;
                Assert.True(Spin(server, out peerId, out data, out length), "the server received it");
                Assert.Equal(length, 3, "all three bytes");
                Assert.True(peerId > 0, "and the sender got an id");

                server.Send(peerId, new byte[] { 9 }, 1);
                int backId; byte[] back; int backLength;
                Assert.True(Spin(client, out backId, out back, out backLength), "the client heard the reply");
                Assert.Equal(backId, 0, "which is always peer 0 to a client");

                // A second datagram from the same endpoint must not consume a second slot.
                client.Send(0, new byte[] { 4 }, 1);
                int again;
                Assert.True(Spin(server, out again, out data, out length), "second datagram arrived");
                Assert.Equal(again, peerId, "same endpoint, same id");

                server.Dispose();
                client.Dispose();
            });

            TestRunner.Add("udp/stray datagrams cannot wedge the server permanently", () =>
            {
                // Every source address that ever sent a byte used to hold a peer slot for the lifetime
                // of the process. A handful of port scans, and every real player afterwards was dropped
                // in silence - which from the outside looks exactly like an endless loading screen.
                string error;
                int port = FreePort();
                UdpTransport server = UdpTransport.CreateServer(port, out error);
                Assert.True(server != null, "server bound: " + error);

                double now = 0.0;
                server.Update(now);

                // More strangers than there are slots, each from its own ephemeral port.
                for (int i = 0; i < Protocol.MaxPeerId + 4; i++)
                {
                    using (Socket stray = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        stray.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                        stray.SendTo(new byte[] { 7 }, new IPEndPoint(IPAddress.Loopback, port));
                    }
                    int id; byte[] d; int len;
                    Spin(server, out id, out d, out len);
                }

                // Ten seconds later none of them are live, so a real player must still get in.
                now += 11.0;
                server.Update(now);

                UdpTransport player = UdpTransport.CreateClient("127.0.0.1", port, out error);
                Assert.True(player != null, "player made: " + error);
                player.Send(0, new byte[] { 1 }, 1);

                int peerId; byte[] data; int length;
                Assert.True(Spin(server, out peerId, out data, out length), "the player's datagram arrived");
                Assert.True(peerId > 0, "and it was given a slot rather than silently dropped");

                server.Dispose();
                player.Dispose();
            });

            TestRunner.Add("udp/the client accepts a reply from an address it did not write to", () =>
            {
                // Joining a host on your own LAN through its public address: the router hands the packet
                // over and the host answers from its private address. Insisting the reply come from the
                // address we wrote to dropped it on the floor and the client hung forever.
                string error;
                int port = FreePort();
                UdpTransport client = UdpTransport.CreateClient("203.0.113.9", port, out error);
                Assert.True(client != null, "client made: " + error);
                client.Send(0, new byte[] { 1 }, 1);      // goes nowhere, which is the point

                int clientPort = client.LocalPort;
                using (Socket elsewhere = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    elsewhere.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    elsewhere.SendTo(new byte[] { 5, 5 }, new IPEndPoint(IPAddress.Loopback, clientPort));
                }

                int peerId; byte[] data; int length;
                Assert.True(Spin(client, out peerId, out data, out length),
                    "the reply was accepted even though it came from somewhere else");
                Assert.Equal(length, 2, "intact");

                client.Dispose();
            });
        }

        /// <summary>UDP on loopback is quick but not instant; give it a few hundred passes.</summary>
        static bool Spin(UdpTransport transport, out int peerId, out byte[] data, out int length)
        {
            for (int i = 0; i < 400; i++)
            {
                if (transport.Poll(out peerId, out data, out length)) return true;
                System.Threading.Thread.Sleep(2);
            }
            peerId = -1; data = null; length = 0;
            return false;
        }
    }
}
