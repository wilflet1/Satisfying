using System;
using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>
    /// Minimal reliability layer riding on top of the unreliable snapshot stream: numbered payloads are
    /// repeated until the far end acks them, duplicates are dropped, and anything larger than a
    /// fragment is split and reassembled. Used for the handful of things that must not be lost -
    /// spawns, deaths, score, hit confirmations and tuning pushes.
    /// </summary>
    public sealed class ReliableChannel
    {
        public const int FragmentSize = 700;

        sealed class Pending
        {
            public uint Seq;
            public byte[] Wire;
            public double LastSent;
        }

        sealed class Assembly
        {
            public byte[][] Parts;
            public int Have;
        }

        readonly List<Pending> _pending = new List<Pending>();
        uint _nextSeq = 1;
        uint _nextMessageId = 1;

        // receive side
        uint _highestReceived;
        uint _ackBase;                     // every seq up to here has arrived
        readonly HashSet<uint> _received = new HashSet<uint>();
        readonly Dictionary<uint, Assembly> _assemblies = new Dictionary<uint, Assembly>();

        public int PendingCount { get { return _pending.Count; } }

        public void Reset()
        {
            _pending.Clear();
            _received.Clear();
            _assemblies.Clear();
            _nextSeq = 1;
            _nextMessageId = 1;
            _highestReceived = 0;
            _ackBase = 0;
        }

        public void Queue(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return;

            if (payload.Length <= FragmentSize)
            {
                byte[] wire = new byte[payload.Length + 1];
                wire[0] = 0;
                Buffer.BlockCopy(payload, 0, wire, 1, payload.Length);
                QueueWire(wire);
                return;
            }

            int count = (payload.Length + FragmentSize - 1) / FragmentSize;
            if (count > 255) throw new InvalidOperationException("reliable payload too large: " + payload.Length);
            uint id = _nextMessageId++;
            for (int i = 0; i < count; i++)
            {
                int offset = i * FragmentSize;
                int size = Math.Min(FragmentSize, payload.Length - offset);
                byte[] wire = new byte[size + 7];
                wire[0] = 1;
                wire[1] = (byte)(id & 0xFF);
                wire[2] = (byte)((id >> 8) & 0xFF);
                wire[3] = (byte)((id >> 16) & 0xFF);
                wire[4] = (byte)((id >> 24) & 0xFF);
                wire[5] = (byte)i;
                wire[6] = (byte)count;
                Buffer.BlockCopy(payload, offset, wire, 7, size);
                QueueWire(wire);
            }
        }

        void QueueWire(byte[] wire)
        {
            Pending p = new Pending();
            p.Seq = _nextSeq++;
            p.Wire = wire;
            p.LastSent = -1000.0;
            _pending.Add(p);
        }

        /// <summary>Writes as many due payloads as fit into the packet.</summary>
        public void WritePending(NetBuffer b, double now, int budgetBytes)
        {
            int countPos = b.BitPosition;
            b.WriteBits(0u, 5);
            int written = 0;
            int used = 0;

            for (int i = 0; i < _pending.Count && written < 31; i++)
            {
                Pending p = _pending[i];
                if (now - p.LastSent < Protocol.ReliableResendInterval) continue;
                int cost = p.Wire.Length + 6;
                if (used + cost > budgetBytes && written > 0) break;
                if (used + cost > budgetBytes) continue;      // never stall behind an oversized entry
                p.LastSent = now;
                b.WriteUInt(p.Seq);
                b.WriteBytes(p.Wire, 0, p.Wire.Length);
                used += cost;
                written++;
            }

            int end = b.BitPosition;
            b.SeekBits(countPos);
            b.WriteBits((uint)written, 5);
            b.SeekBits(end);
        }

        /// <summary>Reads payloads out of a packet, dropping duplicates and reassembling fragments.</summary>
        public void ReadInto(NetBuffer b, List<byte[]> fresh)
        {
            int count = (int)b.ReadBits(5);
            for (int i = 0; i < count; i++)
            {
                uint seq = b.ReadUInt();
                byte[] wire = b.ReadBytes();
                if (wire.Length == 0) continue;
                if (_received.Contains(seq)) continue;

                _received.Add(seq);
                if (seq > _highestReceived) _highestReceived = seq;
                while (_received.Contains(_ackBase + 1u)) _ackBase++;
                TrimReceived();

                if (wire[0] == 0)
                {
                    byte[] payload = new byte[wire.Length - 1];
                    Buffer.BlockCopy(wire, 1, payload, 0, payload.Length);
                    fresh.Add(payload);
                    continue;
                }

                uint id = (uint)(wire[1] | (wire[2] << 8) | (wire[3] << 16) | (wire[4] << 24));
                int index = wire[5];
                int total = wire[6];
                Assembly asm;
                if (!_assemblies.TryGetValue(id, out asm))
                {
                    asm = new Assembly();
                    asm.Parts = new byte[total][];
                    _assemblies[id] = asm;
                }
                if (index >= asm.Parts.Length || asm.Parts[index] != null) continue;

                byte[] part = new byte[wire.Length - 7];
                Buffer.BlockCopy(wire, 7, part, 0, part.Length);
                asm.Parts[index] = part;
                asm.Have++;

                if (asm.Have < asm.Parts.Length) continue;

                int size = 0;
                for (int k = 0; k < asm.Parts.Length; k++) size += asm.Parts[k].Length;
                byte[] whole = new byte[size];
                int offset = 0;
                for (int k = 0; k < asm.Parts.Length; k++)
                {
                    Buffer.BlockCopy(asm.Parts[k], 0, whole, offset, asm.Parts[k].Length);
                    offset += asm.Parts[k].Length;
                }
                _assemblies.Remove(id);
                fresh.Add(whole);
            }
        }

        void TrimReceived()
        {
            if (_received.Count <= 512) return;
            List<uint> stale = new List<uint>();
            foreach (uint s in _received) if (s + 256 < _ackBase) stale.Add(s);
            for (int k = 0; k < stale.Count; k++) _received.Remove(stale[k]);
        }

        /// <summary>
        /// What the receiver reports back: the highest CONTIGUOUS sequence it has. Acking the highest
        /// sequence seen instead would quietly drop a payload that fell in a hole.
        /// </summary>
        public uint AckValue { get { return _ackBase; } }

        public void OnAck(uint ackedSeq)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].Seq <= ackedSeq) _pending.RemoveAt(i);
        }
    }
}
