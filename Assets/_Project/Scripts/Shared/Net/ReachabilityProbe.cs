using System;
using System.Collections.Generic;

namespace Satisfying.Shared
{
    /// <summary>
    /// Answers the only question a host actually cares about: can anyone out there reach me?
    ///
    /// <see cref="PortMapper"/> cannot answer it. All it knows is whether the router accepted a mapping
    /// that IT asked for - so a port forwarded by hand on the router admin page looks identical to a
    /// port that is shut, and the menu says the door is closed while players walk through it.
    ///
    /// Reachability cannot be measured from inside the network, so this uses two outside sources:
    ///
    ///   STUN     A binding request sent FROM THE GAME SOCKET to a public STUN server, which replies
    ///            with the address and port it saw. That is the address to hand out. If the port it
    ///            saw is the port we are bound to, the NAT is passing our port straight through, which
    ///            is what a working forward looks like. If it saw a different port, the router is
    ///            remapping us and no amount of forwarding will help - a definite no.
    ///
    ///   Traffic  A datagram from a public address that we never wrote to first. Nothing else can
    ///            produce that, so it is proof, not inference. It is the only signal here that is
    ///            certain, and it only arrives once somebody actually connects.
    ///
    /// STUN must leave by the game socket or the answer is about some other port and is worthless, so
    /// this type owns no socket. The caller pumps it: ask <see cref="NextRequest"/> what to send, feed
    /// every inbound datagram to <see cref="HandleDatagram"/>, and report senders to <see cref="NoteInbound"/>.
    /// </summary>
    public sealed class ReachabilityProbe
    {
        public enum Verdict
        {
            /// <summary>No answer yet.</summary>
            Probing,
            /// <summary>Someone on the internet reached us. Proof.</summary>
            Confirmed,
            /// <summary>The outside sees our port unchanged - a forward would look exactly like this.</summary>
            PortPreserved,
            /// <summary>The outside sees a different port. Inbound cannot work, forwarded or not.</summary>
            PortRemapped,
            /// <summary>No STUN server answered. We know nothing.</summary>
            NoAnswer
        }

        /// <summary>
        /// Public STUN servers, tried in order. They are contacted from the game socket and told
        /// nothing except that this address exists - which is the same thing every website learns.
        /// Replace the list to point at your own if you would rather not talk to these.
        /// </summary>
        public static string[] Servers =
        {
            "stun.l.google.com:19302",
            "stun1.l.google.com:19302",
            "stun.cloudflare.com:3478"
        };

        const int HeaderSize = 20;
        const uint MagicCookie = 0x2112A442u;
        const ushort BindingRequest = 0x0001;
        const ushort BindingResponse = 0x0101;
        const ushort AttrMappedAddress = 0x0001;
        const ushort AttrXorMappedAddress = 0x0020;

        /// <summary>Retry cadence. Slow: this is a background question, not something anyone waits on.</summary>
        const double RetrySeconds = 2.0;
        const int MaxAttempts = 6;

        readonly int _localPort;
        readonly byte[] _transactionId = new byte[12];
        readonly Random _random;

        double _nextSendAt;
        int _attempts;
        int _serverIndex;
        bool _sawInboundFromPublic;

        public Verdict State { get; private set; }
        /// <summary>The address to hand out, once anything has told us what it is. Null until then.</summary>
        public string ExternalAddress { get; private set; }
        public int ExternalPort { get; private set; }
        /// <summary>Which STUN server answered, for the "why is it saying that" case.</summary>
        public string AnsweredBy { get; private set; }

        public ReachabilityProbe(int localPort) : this(localPort, new Random()) { }

        /// <summary>Seeded overload so a test gets the same transaction id every run.</summary>
        public ReachabilityProbe(int localPort, Random random)
        {
            _localPort = localPort;
            _random = random;
            State = Verdict.Probing;
            _random.NextBytes(_transactionId);
        }

        /// <summary>True once we can stop asking: either we know, or we have run out of servers to ask.</summary>
        public bool Settled
        {
            get { return State == Verdict.Confirmed || State == Verdict.PortRemapped || State == Verdict.NoAnswer; }
        }

        /// <summary>
        /// A datagram arrived from a public address we never wrote to first. That cannot happen unless
        /// the port is genuinely open, so it outranks anything STUN inferred and is never revised.
        /// </summary>
        public void NoteInbound(string sourceAddress)
        {
            if (_sawInboundFromPublic) return;
            if (!IsPublicAddress(sourceAddress)) return;
            _sawInboundFromPublic = true;
            State = Verdict.Confirmed;
        }

        /// <summary>
        /// What to send next, if anything. The caller resolves the host and sends the payload out of
        /// the game socket - which is the whole point, since the answer is about that socket's port.
        /// </summary>
        public bool NextRequest(double now, out string host, out int port, out byte[] payload)
        {
            host = null;
            port = 0;
            payload = null;

            if (Settled) return false;
            if (Servers == null || Servers.Length == 0) { State = Verdict.NoAnswer; return false; }
            if (now < _nextSendAt) return false;

            if (_attempts >= MaxAttempts)
            {
                // Nothing answered. Say so rather than guessing - a wrong "closed" is what got us here.
                if (State == Verdict.Probing) State = Verdict.NoAnswer;
                return false;
            }

            string entry = Servers[_serverIndex % Servers.Length];
            if (!SplitHostPort(entry, out host, out port)) { _serverIndex++; _attempts++; return false; }

            payload = BuildBindingRequest();
            _attempts++;
            _serverIndex++;                       // rotate: one dead server must not stall the whole probe
            _nextSendAt = now + RetrySeconds;
            return true;
        }

        /// <summary>
        /// Offer an inbound datagram to the probe. Returns true if it was a STUN reply and the caller
        /// must NOT treat it as game traffic - the server would otherwise hand a STUN server a player
        /// slot and wait for it to say hello.
        /// </summary>
        public bool HandleDatagram(byte[] data, int length, string fromAddress)
        {
            if (!IsStunResponse(data, length)) return false;

            // Only our own transaction. Anything else is a stray reply to a previous run of the game.
            for (int i = 0; i < 12; i++)
                if (data[8 + i] != _transactionId[i]) return true;   // consumed, but not ours

            string address;
            int port;
            if (!ParseMappedAddress(data, length, out address, out port)) return true;

            ExternalAddress = address;
            ExternalPort = port;
            AnsweredBy = fromAddress;

            // Proof from real traffic always wins; never talk it back down to an inference.
            if (State == Verdict.Confirmed) return true;

            State = port == _localPort ? Verdict.PortPreserved : Verdict.PortRemapped;
            return true;
        }

        /// <summary>What the menu should say. Kept here so the host UI and any headless log agree.</summary>
        public string Describe(int gamePort)
        {
            switch (State)
            {
                case Verdict.Confirmed:
                    return "confirmed open - a player reached you from the internet";
                case Verdict.PortPreserved:
                    return "UDP " + gamePort + " looks forwarded - unconfirmed until someone joins";
                case Verdict.PortRemapped:
                    return "your router is remapping UDP " + gamePort + " to " + ExternalPort +
                           " - inbound cannot work until that stops";
                case Verdict.NoAnswer:
                    return "could not reach a STUN server to check from outside";
                default:
                    return "checking whether " + gamePort + " is reachable from outside...";
            }
        }

        // ------------------------------------------------------------------ STUN wire format
        public byte[] BuildBindingRequest()
        {
            byte[] packet = new byte[HeaderSize];
            packet[0] = (byte)(BindingRequest >> 8);
            packet[1] = (byte)(BindingRequest & 0xFF);
            packet[2] = 0;                       // no attributes
            packet[3] = 0;
            packet[4] = (byte)((MagicCookie >> 24) & 0xFF);
            packet[5] = (byte)((MagicCookie >> 16) & 0xFF);
            packet[6] = (byte)((MagicCookie >> 8) & 0xFF);
            packet[7] = (byte)(MagicCookie & 0xFF);
            Array.Copy(_transactionId, 0, packet, 8, 12);
            return packet;
        }

        public static bool IsStunResponse(byte[] data, int length)
        {
            if (data == null || length < HeaderSize) return false;
            ushort type = (ushort)((data[0] << 8) | data[1]);
            if (type != BindingResponse) return false;
            uint cookie = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
            return cookie == MagicCookie;
        }

        /// <summary>
        /// Walks the attribute list for the address the server saw. XOR-MAPPED-ADDRESS is the modern
        /// one and is obfuscated precisely so middleboxes cannot helpfully rewrite it; MAPPED-ADDRESS
        /// is the ancient plain version, still all some servers send.
        /// </summary>
        public static bool ParseMappedAddress(byte[] data, int length, out string address, out int port)
        {
            address = null;
            port = 0;

            int declared = (data[2] << 8) | data[3];
            int end = HeaderSize + declared;
            if (end > length) end = length;         // trust the buffer over the header

            int at = HeaderSize;
            while (at + 4 <= end)
            {
                ushort type = (ushort)((data[at] << 8) | data[at + 1]);
                int size = (data[at + 2] << 8) | data[at + 3];
                int value = at + 4;
                if (value + size > end) break;

                if (type == AttrXorMappedAddress || type == AttrMappedAddress)
                {
                    // value: 1 byte pad, 1 byte family, 2 bytes port, then the address.
                    if (size >= 8 && data[value + 1] == 0x01)      // 0x01 = IPv4
                    {
                        int rawPort = (data[value + 2] << 8) | data[value + 3];
                        byte[] raw = new byte[4];
                        Array.Copy(data, value + 4, raw, 0, 4);

                        if (type == AttrXorMappedAddress)
                        {
                            rawPort ^= (int)(MagicCookie >> 16);
                            raw[0] ^= (byte)((MagicCookie >> 24) & 0xFF);
                            raw[1] ^= (byte)((MagicCookie >> 16) & 0xFF);
                            raw[2] ^= (byte)((MagicCookie >> 8) & 0xFF);
                            raw[3] ^= (byte)(MagicCookie & 0xFF);
                        }

                        port = rawPort & 0xFFFF;
                        address = raw[0] + "." + raw[1] + "." + raw[2] + "." + raw[3];
                        return true;
                    }
                }

                size = (size + 3) & ~3;             // attributes are padded to 4 bytes
                at = value + size;
            }
            return false;
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// Anything routable from the internet. The interesting exclusion is 100.64/10: a carrier-grade
        /// NAT address means the packet came from inside the ISP, so it proves nothing about the world.
        /// </summary>
        public static bool IsPublicAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            string[] parts = address.Split('.');
            if (parts.Length != 4) return false;

            int[] octet = new int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(parts[i], out octet[i]) || octet[i] < 0 || octet[i] > 255) return false;

            if (octet[0] == 10) return false;
            if (octet[0] == 127) return false;
            if (octet[0] == 0) return false;
            if (octet[0] == 172 && octet[1] >= 16 && octet[1] <= 31) return false;
            if (octet[0] == 192 && octet[1] == 168) return false;
            if (octet[0] == 169 && octet[1] == 254) return false;
            if (octet[0] == 100 && octet[1] >= 64 && octet[1] <= 127) return false;
            if (octet[0] >= 224) return false;                       // multicast and above
            return true;
        }

        public static bool SplitHostPort(string entry, out string host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrEmpty(entry)) return false;

            int colon = entry.LastIndexOf(':');
            if (colon <= 0 || colon == entry.Length - 1) return false;

            host = entry.Substring(0, colon);
            return int.TryParse(entry.Substring(colon + 1), out port) && port > 0 && port <= 65535;
        }
    }
}
