using System;
using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>
    /// Datagram plumbing. The simulation never touches sockets directly, which is what lets the whole
    /// client/server stack run inside a unit test over a simulated lossy link.
    /// peerId 0 always means "the server" when used from a client.
    /// </summary>
    public interface ITransport
    {
        void Update(double now);
        bool Poll(out int peerId, out byte[] data, out int length);
        void Send(int peerId, byte[] data, int length);
        void Forget(int peerId);
        string Describe(int peerId);
    }

    /// <summary>Latency / jitter / packet loss knobs. Exposed in the menu so you can feel 150ms before you ship.</summary>
    public sealed class NetConditions
    {
        [Tune("Network", 0f, 400f, Tip = "One way delay added to everything this machine sends.")]
        public float latencyMs = 0f;

        [Tune("Network", 0f, 120f, Tip = "Random variation added on top of the latency.")]
        public float jitterMs = 0f;

        [Tune("Network", 0f, 40f, Tip = "Percentage of outgoing packets thrown away.")]
        public float lossPercent = 0f;

        [Tune("Network", 0f, 20f, Tip = "Percentage of packets sent twice.")]
        public float duplicatePercent = 0f;

        public bool Enabled { get { return latencyMs > 0.01f || jitterMs > 0.01f || lossPercent > 0.01f || duplicatePercent > 0.01f; } }

        public NetConditions Clone() { return (NetConditions)MemberwiseClone(); }
    }

    /// <summary>
    /// Wraps any transport and degrades it on purpose. Delay is applied on send, so each end contributes
    /// its own one-way latency and the round trip is the sum of both - exactly like the real thing.
    /// </summary>
    public sealed class ConditionedTransport : ITransport
    {
        struct Pending
        {
            public double DueTime;
            public int PeerId;
            public byte[] Data;
            public int Length;
        }

        readonly ITransport _inner;
        readonly List<Pending> _queue = new List<Pending>();
        DeterministicRandom _rng = new DeterministicRandom(0x1234567u);
        double _now;

        public NetConditions Conditions = new NetConditions();

        public ConditionedTransport(ITransport inner) { _inner = inner; }

        public ITransport Inner { get { return _inner; } }

        public void Update(double now)
        {
            _now = now;
            _inner.Update(now);

            // Forward order matters: packets queued in the same frame must leave in the order they were
            // written, otherwise a snapshot can overtake the connect accept that explains it.
            int i = 0;
            while (i < _queue.Count)
            {
                if (_queue[i].DueTime > now) { i++; continue; }
                Pending p = _queue[i];
                _queue.RemoveAt(i);
                _inner.Send(p.PeerId, p.Data, p.Length);
            }
        }

        public bool Poll(out int peerId, out byte[] data, out int length)
        {
            return _inner.Poll(out peerId, out data, out length);
        }

        public void Send(int peerId, byte[] data, int length)
        {
            if (!Conditions.Enabled)
            {
                _inner.Send(peerId, data, length);
                return;
            }

            if (Conditions.lossPercent > 0f && _rng.NextFloat() * 100f < Conditions.lossPercent) return;

            int copies = 1;
            if (Conditions.duplicatePercent > 0f && _rng.NextFloat() * 100f < Conditions.duplicatePercent) copies = 2;

            for (int c = 0; c < copies; c++)
            {
                Pending p;
                p.PeerId = peerId;
                p.Data = new byte[length];
                Buffer.BlockCopy(data, 0, p.Data, 0, length);
                p.Length = length;
                float jitter = Conditions.jitterMs > 0f ? _rng.NextFloat() * Conditions.jitterMs : 0f;
                p.DueTime = _now + (Conditions.latencyMs + jitter) / 1000.0;
                _queue.Add(p);
            }
        }

        public void Forget(int peerId) { _inner.Forget(peerId); }
        public string Describe(int peerId) { return _inner.Describe(peerId); }
    }

    /// <summary>In-process network used by the headless tests (and by "host + local client" play).</summary>
    public sealed class LoopbackNetwork
    {
        sealed class Endpoint : ITransport
        {
            public LoopbackNetwork Net;
            public int Id;
            public readonly Queue<KeyValuePair<int, byte[]>> Inbox = new Queue<KeyValuePair<int, byte[]>>();

            public void Update(double now) { }

            public bool Poll(out int peerId, out byte[] data, out int length)
            {
                if (Inbox.Count == 0) { peerId = -1; data = null; length = 0; return false; }
                KeyValuePair<int, byte[]> item = Inbox.Dequeue();
                peerId = item.Key;
                data = item.Value;
                length = item.Value.Length;
                return true;
            }

            public void Send(int peerId, byte[] data, int length)
            {
                Endpoint dst = Net.Get(peerId);
                if (dst == null) return;
                byte[] copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                dst.Inbox.Enqueue(new KeyValuePair<int, byte[]>(Id, copy));
            }

            public void Forget(int peerId) { }
            public string Describe(int peerId) { return "loopback:" + peerId; }
        }

        readonly Dictionary<int, Endpoint> _endpoints = new Dictionary<int, Endpoint>();

        public ITransport CreateEndpoint(int id)
        {
            Endpoint e = new Endpoint();
            e.Net = this;
            e.Id = id;
            _endpoints[id] = e;
            return e;
        }

        Endpoint Get(int id)
        {
            Endpoint e;
            return _endpoints.TryGetValue(id, out e) ? e : null;
        }
    }
}
