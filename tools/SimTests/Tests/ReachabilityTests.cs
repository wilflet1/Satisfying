using System;
using Satisfying.Shared;

namespace Satisfying.Tests
{
    public static class ReachabilityTests
    {
        /// <summary>
        /// Builds the reply a STUN server would send: our transaction id back, and one XOR-MAPPED-ADDRESS
        /// carrying the address it saw. Written by hand rather than captured, so the expected bytes are
        /// visible in the test rather than hidden in a blob.
        /// </summary>
        static byte[] BindingResponse(byte[] request, string address, int port, bool xored)
        {
            const uint cookie = 0x2112A442u;
            string[] parts = address.Split('.');
            byte[] raw = new byte[4];
            for (int i = 0; i < 4; i++) raw[i] = byte.Parse(parts[i]);

            int wirePort = port;
            if (xored)
            {
                wirePort = port ^ (int)(cookie >> 16);
                raw[0] ^= (byte)((cookie >> 24) & 0xFF);
                raw[1] ^= (byte)((cookie >> 16) & 0xFF);
                raw[2] ^= (byte)((cookie >> 8) & 0xFF);
                raw[3] ^= (byte)(cookie & 0xFF);
            }

            byte[] reply = new byte[20 + 12];
            reply[0] = 0x01; reply[1] = 0x01;              // binding response
            reply[2] = 0; reply[3] = 12;                   // attribute bytes that follow
            for (int i = 0; i < 4; i++) reply[4 + i] = request[4 + i];    // magic cookie
            for (int i = 0; i < 12; i++) reply[8 + i] = request[8 + i];   // transaction id

            int at = 20;
            ushort type = xored ? (ushort)0x0020 : (ushort)0x0001;
            reply[at] = (byte)(type >> 8); reply[at + 1] = (byte)(type & 0xFF);
            reply[at + 2] = 0; reply[at + 3] = 8;          // attribute length
            reply[at + 4] = 0;                             // padding
            reply[at + 5] = 0x01;                          // IPv4
            reply[at + 6] = (byte)((wirePort >> 8) & 0xFF);
            reply[at + 7] = (byte)(wirePort & 0xFF);
            for (int i = 0; i < 4; i++) reply[at + 8 + i] = raw[i];
            return reply;
        }

        static ReachabilityProbe Probe(int port)
        {
            return new ReachabilityProbe(port, new Random(1234));
        }

        public static void Register()
        {
            TestRunner.Add("reach/reads the address a STUN server saw", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                Assert.True(p.NextRequest(0.0, out host, out port, out payload), "it wants to ask someone");
                Assert.Equal(payload.Length, 20, "a binding request is a bare header");

                byte[] reply = BindingResponse(payload, "165.0.84.175", 7777, true);
                Assert.True(p.HandleDatagram(reply, reply.Length, "74.125.0.1"), "the reply was consumed");

                Assert.True(p.ExternalAddress == "165.0.84.175", "decoded the XOR address, got " + p.ExternalAddress);
                Assert.Equal(p.ExternalPort, 7777, "and the port");
            });

            TestRunner.Add("reach/the old plain MAPPED-ADDRESS form still works", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);

                byte[] reply = BindingResponse(payload, "203.0.113.9", 7777, false);
                p.HandleDatagram(reply, reply.Length, "1.2.3.4");

                Assert.True(p.ExternalAddress == "203.0.113.9", "decoded the plain address, got " + p.ExternalAddress);
                Assert.Equal(p.ExternalPort, 7777, "and the port");
            });

            TestRunner.Add("reach/same port out means the forward could be working", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);
                byte[] reply = BindingResponse(payload, "165.0.84.175", 7777, true);
                p.HandleDatagram(reply, reply.Length, "74.125.0.1");

                Assert.True(p.State == ReachabilityProbe.Verdict.PortPreserved, "port preserved, got " + p.State);
                // Deliberately not "open": port preservation is common on NATs with no forward at all.
                Assert.False(p.Settled, "it is not proof, so keep looking");
            });

            TestRunner.Add("reach/a remapped port is a definite no", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);
                byte[] reply = BindingResponse(payload, "165.0.84.175", 51413, true);
                p.HandleDatagram(reply, reply.Length, "74.125.0.1");

                Assert.True(p.State == ReachabilityProbe.Verdict.PortRemapped, "remapped, got " + p.State);
                Assert.True(p.Settled, "and there is no point asking again");
            });

            TestRunner.Add("reach/a real player from outside is proof, and outranks STUN", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);

                // STUN says the router is remapping us - the gloomiest verdict there is.
                byte[] reply = BindingResponse(payload, "165.0.84.175", 51413, true);
                p.HandleDatagram(reply, reply.Length, "74.125.0.1");
                Assert.True(p.State == ReachabilityProbe.Verdict.PortRemapped, "gloomy first");

                // Then somebody actually connects from a public address. Observation beats inference.
                p.NoteInbound("81.2.69.142");
                Assert.True(p.State == ReachabilityProbe.Verdict.Confirmed, "confirmed, got " + p.State);
            });

            TestRunner.Add("reach/a LAN player proves nothing about the internet", () =>
            {
                ReachabilityProbe p = Probe(7777);
                p.NoteInbound("192.168.10.23");
                p.NoteInbound("10.5.0.2");
                p.NoteInbound("172.20.1.1");
                p.NoteInbound("127.0.0.1");
                Assert.True(p.State == ReachabilityProbe.Verdict.Probing, "still knows nothing, got " + p.State);

                // Carrier-grade NAT is the subtle one: routable-looking, but still inside the ISP.
                p.NoteInbound("100.72.14.9");
                Assert.True(p.State == ReachabilityProbe.Verdict.Probing, "CGNAT is not the internet");
            });

            TestRunner.Add("reach/once confirmed it never talks itself back down", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);

                p.NoteInbound("81.2.69.142");
                Assert.True(p.State == ReachabilityProbe.Verdict.Confirmed, "confirmed");

                byte[] reply = BindingResponse(payload, "165.0.84.175", 51413, true);
                p.HandleDatagram(reply, reply.Length, "74.125.0.1");
                Assert.True(p.State == ReachabilityProbe.Verdict.Confirmed, "a late STUN reply cannot undo proof");
            });

            TestRunner.Add("reach/someone else's transaction is ignored", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;
                p.NextRequest(0.0, out host, out port, out payload);

                byte[] reply = BindingResponse(payload, "165.0.84.175", 7777, true);
                reply[9] ^= 0xFF;                       // a stray reply from a previous run

                Assert.True(p.HandleDatagram(reply, reply.Length, "74.125.0.1"), "still swallowed, not a game packet");
                Assert.True(p.ExternalAddress == null, "but nothing was believed");
                Assert.True(p.State == ReachabilityProbe.Verdict.Probing, "and the verdict stands");
            });

            TestRunner.Add("reach/game traffic is never mistaken for STUN", () =>
            {
                ReachabilityProbe p = Probe(7777);
                // Every packet the game sends must fall through to the netcode untouched.
                byte[] packet = new byte[64];
                for (int i = 0; i < packet.Length; i++) packet[i] = (byte)(i * 7);
                Assert.False(p.HandleDatagram(packet, packet.Length, "81.2.69.142"), "not STUN");

                byte[] tiny = new byte[4];
                Assert.False(p.HandleDatagram(tiny, tiny.Length, "81.2.69.142"), "too short to be STUN");
            });

            TestRunner.Add("reach/it gives up rather than claiming the port is shut", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string host; int port; byte[] payload;

                // Nothing ever answers. Walk the clock past every retry.
                double now = 0.0;
                for (int i = 0; i < 20; i++)
                {
                    p.NextRequest(now, out host, out port, out payload);
                    now += 2.5;
                }

                Assert.True(p.State == ReachabilityProbe.Verdict.NoAnswer, "says it does not know, got " + p.State);
                Assert.False(p.NextRequest(now, out host, out port, out payload), "and stops asking");
            });

            TestRunner.Add("reach/rotates servers so one dead host cannot stall it", () =>
            {
                ReachabilityProbe p = Probe(7777);
                string first, second;
                int port; byte[] payload;

                p.NextRequest(0.0, out first, out port, out payload);
                p.NextRequest(5.0, out second, out port, out payload);
                Assert.False(first == second, "asked a different server the second time");
            });

            TestRunner.Add("reach/host:port parsing", () =>
            {
                string host; int port;
                Assert.True(ReachabilityProbe.SplitHostPort("stun.l.google.com:19302", out host, out port), "parsed");
                Assert.True(host == "stun.l.google.com", "host");
                Assert.Equal(port, 19302, "port");

                Assert.False(ReachabilityProbe.SplitHostPort("stun.l.google.com", out host, out port), "no port");
                Assert.False(ReachabilityProbe.SplitHostPort("stun.l.google.com:", out host, out port), "empty port");
                Assert.False(ReachabilityProbe.SplitHostPort("", out host, out port), "empty");
            });
        }
    }
}
