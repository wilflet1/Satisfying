using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// What people actually paste. The host is told to hand out "1.2.3.4:7777" and the copy button
    /// puts exactly that on their clipboard, so that string has to work in the join box.
    /// </summary>
    public static class NetAddressTests
    {
        static void Check(string input, string expectHost, int expectPort)
        {
            string host;
            int port;
            Assert.True(NetAddress.TryParse(input, 7777, out host, out port), "parsed \"" + input + "\"");
            Assert.True(host == expectHost, "\"" + input + "\" -> host " + expectHost + ", got " + host);
            Assert.Equal(port, expectPort, "\"" + input + "\" -> port");
        }

        public static void Register()
        {
            TestRunner.Add("address/the string the copy button produces works in the join box", () =>
            {
                Check("203.0.113.7:7777", "203.0.113.7", 7777);
                Check("203.0.113.7:9000", "203.0.113.7", 9000);
                Check("203.0.113.7", "203.0.113.7", 7777);
            });

            TestRunner.Add("address/whitespace and pasted links are tolerated", () =>
            {
                Check("  192.168.1.20 ", "192.168.1.20", 7777);
                Check("192.168.1.20 : 9001 ", "192.168.1.20", 9001);
                Check("udp://192.168.1.20:9001", "192.168.1.20", 9001);
                Check("http://myserver.example.com:9001/join", "myserver.example.com", 9001);
                Check("myserver.example.com", "myserver.example.com", 7777);
            });

            TestRunner.Add("address/nonsense leaves the default port rather than inventing one", () =>
            {
                Check("192.168.1.20:", "192.168.1.20", 7777);
                Check("192.168.1.20:not-a-port", "192.168.1.20", 7777);
                Check("192.168.1.20:99999", "192.168.1.20", 7777);
                Check("192.168.1.20:0", "192.168.1.20", 7777);
            });

            TestRunner.Add("address/an empty box is refused instead of resolving to something", () =>
            {
                string host;
                int port;
                Assert.False(NetAddress.TryParse("", 7777, out host, out port), "empty");
                Assert.False(NetAddress.TryParse("   ", 7777, out host, out port), "spaces");
                Assert.False(NetAddress.TryParse("http://", 7777, out host, out port), "a scheme and nothing else");
            });

            TestRunner.Add("address/IPv6 is not mangled on its way to a clear error", () =>
            {
                // The socket layer is IPv4 only. What matters is that the colons in an address are not
                // mistaken for a port separator, so the failure that follows names the real problem.
                Check("[::1]:7777", "::1", 7777);
                Check("[fe80::1]:9000", "fe80::1", 9000);
                Check("::1", "::1", 7777);
            });

            TestRunner.Add("net/the host's own join does not count as somebody reaching it", () =>
            {
                // Hosting connects to its own server over loopback like anybody else, so the panel
                // that exists to answer "are their packets arriving" counted the host itself. It read
                // "1 connection attempt arrived - last one accepted" on a machine nobody had ever
                // tried to join, and kept reading it while a player sat there failing to connect -
                // which is worse than saying nothing, because it sends you looking at the wrong end.
                Assert.True(NetServer.IsLoopback("127.0.0.1:7777"), "IPv4 loopback is yourself");
                Assert.True(NetServer.IsLoopback("[::1]:7777"), "and so is IPv6 loopback");
                Assert.True(NetServer.IsLoopback("loopback:2"), "and so is the in-process link");
                Assert.True(NetServer.IsLoopback(null), "an address we cannot read is not a visitor");

                Assert.True(!NetServer.IsLoopback("81.2.69.142:51413"), "a public address is somebody else");
                Assert.True(!NetServer.IsLoopback("192.168.1.40:7777"), "so is another machine on the LAN");
            });
        }
    }
}
