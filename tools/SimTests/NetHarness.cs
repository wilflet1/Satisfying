using System;
using System.Collections.Generic;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>Records everything the server tells a client, so tests can assert on real gameplay outcomes.</summary>
    public sealed class TestSink : IEventSink
    {
        public NetClient Client;
        public readonly List<string> Log = new List<string>();
        public int HitsConfirmed;
        public int HeadshotsConfirmed;
        public float DamageDealt;
        public int Kills;
        public int Deaths;
        public int Spawns;
        public int RemoteShots;
        public bool GotTuning;

        public void OnPlayerJoined(int peerId, string name) { Log.Add("join " + peerId + " " + name); }
        public void OnPlayerLeft(int peerId, DisconnectReason reason) { Log.Add("left " + peerId); }

        public void OnSpawn(int peerId, Vec3 position, float yaw)
        {
            Log.Add("spawn " + peerId);
            if (Client != null && peerId == Client.PeerId)
            {
                Spawns++;
                Client.ForceSpawn(position, yaw);
            }
        }

        public void OnDeath(int victim, int killer, HitZone zone, float distance)
        {
            Log.Add("death " + victim + " by " + killer + " " + zone);
            if (Client != null && victim == Client.PeerId) Deaths++;
            if (Client != null && killer == Client.PeerId) Kills++;
        }

        public int TargetHits;
        public float LastTargetDistance;

        public void OnTargetHit(HitZone zone, float distance)
        {
            TargetHits++;
            LastTargetDistance = distance;
        }

        public Vec3 LastHitPoint;

        public void OnHitConfirm(int target, HitZone zone, float damage, bool killed, Vec3 point)
        {
            HitsConfirmed++;
            if (zone == HitZone.Head) HeadshotsConfirmed++;
            DamageDealt += damage;
            LastHitPoint = point;
        }

        public int TimesDamaged;
        public HitZone LastDamagedZone;
        public byte LastDamagedByWeapon;
        public float DamageTaken;

        public void OnDamaged(int attacker, HitZone zone, byte weaponIndex, float damage, bool killed)
        {
            TimesDamaged++;
            LastDamagedZone = zone;
            LastDamagedByWeapon = weaponIndex;
            DamageTaken += damage;
        }

        public void OnScore(int peerId, int kills, int deaths) { }

        public int ActiveZone = -1;
        public float ZoneSecondsLeft;
        public int ZoneHolder = KothState.Nobody;
        public int ZoneChanges;

        public int GrenadeUpdates;
        public int Blasts;
        public Vec3 LastBlast;
        public Vec3 LastGrenade;
        public int LastGrenadeBounces;

        public void OnGrenade(int id, int owner, Vec3 position, float fuse, int bounces, SurfaceKind surface)
        {
            GrenadeUpdates++;
            LastGrenade = position;
            LastGrenadeBounces = bounces;
        }

        public void OnBlast(Vec3 position) { Blasts++; LastBlast = position; }

        public void OnZone(int zone, float secondsLeft, int holder)
        {
            if (zone != ActiveZone) ZoneChanges++;
            ActiveZone = zone;
            ZoneSecondsLeft = secondsLeft;
            ZoneHolder = holder;
        }
        public void OnMatchPhase(MatchPhase phase, float timer, int winner) { Log.Add("phase " + phase); }

        public void OnTuning(string tuningText)
        {
            GotTuning = true;
            if (Client != null) TuningSerializer.FromText(Client.Tuning, tuningText);
        }

        public int RemoteShotsOnPlayers;

        public void OnRemoteShot(int shooter, Vec3 origin, Vec3 direction, byte weaponIndex, bool hit,
                                 Vec3 hitPoint, bool hitPlayer)
        {
            RemoteShots++;
            if (hitPlayer) RemoteShotsOnPlayers++;
        }

        public int WindowsBroken;
        public void OnWindowBroken(int windowIndex, Vec3 centre)
        {
            WindowsBroken++;
            Log.Add("window " + windowIndex);
        }
    }

    /// <summary>Bot input: a delegate per tick, so a test can describe behaviour in one lambda.</summary>
    public sealed class BotInput : IInputSource
    {
        public Func<uint, InputCommand> Behaviour;

        public InputCommand Sample(uint tick, float dt)
        {
            return Behaviour != null ? Behaviour(tick) : InputCommand.Default(tick);
        }
    }

    /// <summary>Runs a real server and real clients over a simulated link, in fixed time steps.</summary>
    public sealed class NetHarness
    {
        public const float StepDt = 1f / 128f;

        public readonly LoopbackNetwork Network = new LoopbackNetwork();
        public readonly NetServer Server;
        public readonly ConditionedTransport ServerTransport;
        public readonly BoxWorld World;
        public readonly List<NetClient> Clients = new List<NetClient>();
        public readonly List<ConditionedTransport> ClientTransports = new List<ConditionedTransport>();
        public readonly List<TestSink> Sinks = new List<TestSink>();
        public readonly List<BotInput> Bots = new List<BotInput>();

        public double Now;

        public readonly WorldModel Model = new WorldModel();

        public NetHarness(BoxWorld world = null, SpawnSet spawnSet = null, WorldModel model = null)
        {
            if (model != null)
            {
                Model.Windows.AddRange(model.Windows);
                Model.Props.AddRange(model.Props);
                Model.Targets.AddRange(model.Targets);
            }
            World = world ?? BoxWorld.FlatGround(80f);
            SpawnSet spawns = spawnSet;
            if (spawns == null)
            {
                spawns = new SpawnSet();
                spawns.Add(new Vec3(0f, 0f, -8f), 0f);
                spawns.Add(new Vec3(0f, 0f, 8f), 180f);
                spawns.Add(new Vec3(8f, 0f, 0f), 270f);
                spawns.Add(new Vec3(-8f, 0f, 0f), 90f);
            }

            ServerTransport = new ConditionedTransport(Network.CreateEndpoint(0));
            Server = new NetServer(ServerTransport, World, spawns, new GameTuning(), Model);
        }

        public NetClient AddClient(string name)
        {
            int id = Clients.Count + 1;
            ConditionedTransport transport = new ConditionedTransport(Network.CreateEndpoint(id));
            NetClient client = new NetClient(transport, World);
            client.Model = Model;
            TestSink sink = new TestSink();
            sink.Client = client;
            client.Sink = sink;
            BotInput bot = new BotInput();
            client.InputSource = bot;

            ClientTransports.Add(transport);
            Clients.Add(client);
            Sinks.Add(sink);
            Bots.Add(bot);

            client.Connect(Now, name);
            return client;
        }

        /// <summary>
        /// Kills a player outright, through the server's own damage path, so everything that hangs off
        /// dying happens - including dropping a grenade they had the pin out of. There is no way to
        /// arrange this from the outside: a player cannot shoot themselves.
        /// </summary>
        public void KillHolder(int index)
        {
            Server.KillForTest(Clients[index].PeerId);
        }

        public void SetConditions(float latencyMs, float jitterMs, float lossPercent)
        {
            ServerTransport.Conditions.latencyMs = latencyMs;
            ServerTransport.Conditions.jitterMs = jitterMs;
            ServerTransport.Conditions.lossPercent = lossPercent;
            for (int i = 0; i < ClientTransports.Count; i++)
            {
                ClientTransports[i].Conditions.latencyMs = latencyMs;
                ClientTransports[i].Conditions.jitterMs = jitterMs;
                ClientTransports[i].Conditions.lossPercent = lossPercent;
            }
        }

        public void Advance(float seconds)
        {
            int steps = MathK.Max(1, MathK.RoundToInt(seconds / StepDt));
            for (int i = 0; i < steps; i++)
            {
                Now += StepDt;
                Server.Update(Now, StepDt);
                for (int c = 0; c < Clients.Count; c++) Clients[c].Update(Now, StepDt);
            }
        }

        /// <summary>
        /// Runs the clock until every client has finished its handshake. On a badly degraded link that
        /// can take several seconds, and a test that assumes otherwise measures the handshake instead
        /// of whatever it meant to measure.
        /// </summary>
        public bool WaitForConnect(float timeoutSeconds = 12f)
        {
            float waited = 0f;
            while (waited < timeoutSeconds)
            {
                bool all = Clients.Count > 0;
                for (int i = 0; i < Clients.Count; i++) if (Clients[i].State != NetClient.Status.Connected) all = false;
                if (all) return true;
                Advance(0.1f);
                waited += 0.1f;
            }
            return false;
        }

        public NetServer.ServerPlayer ServerPlayerOf(NetClient client)
        {
            return Server.Find(client.PeerId);
        }

        /// <summary>Distance between what the client predicted for a tick and what the server actually simulated.</summary>
        public float ConvergenceError(NetClient client)
        {
            NetServer.ServerPlayer sp = ServerPlayerOf(client);
            if (sp == null) return 999f;
            PlayerSimState predicted;
            if (!client.TryGetPredictedAt(sp.LastExecutedTick, out predicted)) return 999f;
            return Vec3.Distance(predicted.Position, sp.Sim.Position);
        }
    }
}
