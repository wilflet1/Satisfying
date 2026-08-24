using System.Net;
using System.Net.Sockets;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// The whole stack over real UDP: a real NetServer and a real NetClient, talking through
    /// UdpTransport on loopback rather than the in-process link every other test uses. This is the
    /// path a joining player takes, and until now nothing exercised it end to end.
    /// </summary>
    public static class RealSocketTests
    {
        static int FreePort()
        {
            using (Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                return ((IPEndPoint)probe.LocalEndPoint).Port;
            }
        }

        public static void Register()
        {
            TestRunner.Add("real/a client connects, spawns and is simulated over an actual socket", () =>
            {
                string error;
                int port = FreePort();

                UdpTransport serverSocket = UdpTransport.CreateServer(port, out error);
                Assert.True(serverSocket != null, "server bound: " + error);

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, 0f), 0f);
                spawns.Add(new Vec3(6f, 0f, 0f), 180f);

                BoxWorld world = BoxWorld.FlatGround(80f);
                NetServer server = new NetServer(serverSocket, world, spawns, new GameTuning(), new WorldModel());
                server.Tuning.match.warmupTime = 0f;

                UdpTransport clientSocket = UdpTransport.CreateClient("127.0.0.1", port, out error);
                Assert.True(clientSocket != null, "client made: " + error);

                NetClient client = new NetClient(clientSocket, world);
                BotInput input = new BotInput();
                input.Behaviour = tick => { InputCommand c = InputCommand.Default(tick); c.MoveY = 1f; return c; };
                client.InputSource = input;

                double now = 0.0;
                client.Connect(now, "over the wire");

                // Real sockets need real time to hand the datagrams over.
                const float step = 1f / 64f;
                for (int i = 0; i < 400 && client.State != NetClient.Status.Connected; i++)
                {
                    now += step;
                    server.Update(now, step);
                    client.Update(now, step);
                    System.Threading.Thread.Sleep(1);
                }

                Assert.True(client.State == NetClient.Status.Connected,
                    "connected over a real socket, got " + client.State);
                Assert.Greater(server.ConnectAttemptsSeen, 0.5f, "and the server counted the attempt");

                Vec3 start = client.Predicted.Position;
                for (int i = 0; i < 200; i++)
                {
                    now += step;
                    server.Update(now, step);
                    client.Update(now, step);
                    System.Threading.Thread.Sleep(1);
                }

                Assert.True(client.Connected, "still connected after a few seconds of play");
                NetServer.ServerPlayer sp = server.Find(client.PeerId);
                Assert.True(sp != null, "the server has a player for it");
                Assert.Greater(Vec3.Distance(client.Predicted.Position, start), 1f, "the client moved");
                Assert.Less(Vec3.Distance(client.Predicted.Position, sp.Sim.Position), 0.5f,
                    "and the server agrees where it is");

                client.Disconnect();
                server.Shutdown();
                serverSocket.Dispose();
                clientSocket.Dispose();
            });

            TestRunner.Add("real/two clients on one server see each other over real sockets", () =>
            {
                string error;
                int port = FreePort();

                UdpTransport serverSocket = UdpTransport.CreateServer(port, out error);
                Assert.True(serverSocket != null, "server bound: " + error);

                SpawnSet spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, -6f), 0f);
                spawns.Add(new Vec3(0f, 0f, 6f), 180f);

                BoxWorld world = BoxWorld.FlatGround(80f);
                NetServer server = new NetServer(serverSocket, world, spawns, new GameTuning(), new WorldModel());
                server.Tuning.match.warmupTime = 0f;

                UdpTransport aSocket = UdpTransport.CreateClient("127.0.0.1", port, out error);
                UdpTransport bSocket = UdpTransport.CreateClient("127.0.0.1", port, out error);
                NetClient a = new NetClient(aSocket, world);
                NetClient b = new NetClient(bSocket, world);
                a.InputSource = new BotInput();
                b.InputSource = new BotInput();

                double now = 0.0;
                a.Connect(now, "alpha");
                b.Connect(now, "bravo");

                const float step = 1f / 64f;
                for (int i = 0; i < 600; i++)
                {
                    now += step;
                    server.Update(now, step);
                    a.Update(now, step);
                    b.Update(now, step);
                    System.Threading.Thread.Sleep(1);
                }

                Assert.True(a.Connected && b.Connected, "both connected");
                Assert.True(a.PeerId != b.PeerId, "and were given different ids");
                Assert.Equal(server.ActiveCount, 2, "the server has both");
                Assert.True(a.Remotes.ContainsKey(b.PeerId), "alpha sees bravo");
                Assert.True(b.Remotes.ContainsKey(a.PeerId), "bravo sees alpha");

                a.Disconnect();
                b.Disconnect();
                server.Shutdown();
                serverSocket.Dispose();
                aSocket.Dispose();
                bSocket.Dispose();
            });
        }
    }
}
