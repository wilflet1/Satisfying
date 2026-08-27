using Satisfying.Shared;

namespace Satisfying.Tests
{
    /// <summary>
    /// There is no router in a test harness, so what is checked here is the part that is actually
    /// easy to get wrong: parsing what routers send back, and the byte layout of a NAT-PMP request.
    /// The samples are real device descriptions, trimmed.
    /// </summary>
    public static class PortMapperTests
    {
        const string DeviceXml =
            "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device>" +
            "<deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>" +
            "<serviceList><service>" +
            "<serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType>" +
            "<controlURL>/ctl/L3F</controlURL></service></serviceList>" +
            "<deviceList><device><serviceList><service>" +
            "<serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>" +
            "<SCPDURL>/gatedesc.xml</SCPDURL>" +
            "<controlURL>/ctl/IPConn</controlURL></service></serviceList></device></deviceList>" +
            "</device></root>";

        public static void Register()
        {
            TestRunner.Add("net/the gateway's WAN service is picked out of its description", () =>
            {
                string type, control;
                Assert.True(PortMapper.FindService(DeviceXml, out type, out control), "found a service");
                Assert.True(type == "urn:schemas-upnp-org:service:WANIPConnection:1", "the WAN one, got " + type);
                Assert.True(control == "/ctl/IPConn", "with its control url, got " + control);
            });

            TestRunner.Add("net/a description with no WAN service is refused rather than guessed at", () =>
            {
                string type, control;
                string xml = DeviceXml.Replace("WANIPConnection:1", "WANCableLinkConfig:1");
                Assert.False(PortMapper.FindService(xml, out type, out control), "no service");
            });

            TestRunner.Add("net/relative control urls are resolved against the description", () =>
            {
                Assert.True(PortMapper.AbsoluteUrl("http://192.168.1.1:5000/rootDesc.xml", "/ctl/IPConn")
                            == "http://192.168.1.1:5000/ctl/IPConn", "made absolute");
                Assert.True(PortMapper.AbsoluteUrl("http://192.168.1.1:5000/rootDesc.xml", "http://10.0.0.1/x")
                            == "http://10.0.0.1/x", "an absolute one is left alone");
            });

            TestRunner.Add("net/the SSDP location header survives the usual formatting", () =>
            {
                string reply = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\n" +
                               "Location: http://192.168.0.1:1780/InternetGatewayDevice.xml\r\n" +
                               "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
                Assert.True(PortMapper.HeaderValue(reply, "LOCATION") ==
                            "http://192.168.0.1:1780/InternetGatewayDevice.xml", "case insensitive, colon aware");
                Assert.True(PortMapper.HeaderValue(reply, "SERVER") == null, "and absent headers are null");
            });

            TestRunner.Add("net/an answer from a printer is not mistaken for the gateway", () =>
            {
                // The first thing to answer an SSDP search is often a media server or a printer.
                // Taking its LOCATION and asking it to forward a port gets you nowhere slowly.
                string printer = "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.0.50:80/desc.xml\r\n" +
                                 "ST: urn:schemas-upnp-org:device:Printer:1\r\n\r\n";
                string gateway = "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.0.1:1780/gw.xml\r\n" +
                                 "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

                Assert.True(PortMapper.HeaderValue(printer, "ST").IndexOf("Printer") >= 0, "the printer says so");
                Assert.True(PortMapper.HeaderValue(gateway, "ST").IndexOf("InternetGatewayDevice") >= 0,
                    "and the gateway says so");
                Assert.True(PortMapper.HeaderValue(gateway, "LOCATION") == "http://192.168.0.1:1780/gw.xml",
                    "which is the one worth fetching");
            });

            TestRunner.Add("net/the external address is read out of the SOAP reply", () =>
            {
                string soap = "<s:Envelope><s:Body><u:GetExternalIPAddressResponse>" +
                              "<NewExternalIPAddress>203.0.113.7</NewExternalIPAddress>" +
                              "</u:GetExternalIPAddressResponse></s:Body></s:Envelope>";
                Assert.True(PortMapper.Between(soap, "<NewExternalIPAddress>", "</NewExternalIPAddress>")
                            == "203.0.113.7", "read the address");
            });

            TestRunner.Add("net/the mapping is renewed before the lease it asked for runs out", () =>
            {
                // A mapping carries a lease so it disappears by itself if the game crashes. It was
                // never renewed, so after two hours of hosting the router took the forward away and
                // nothing said so: the host went on reporting the success it had had at startup, and
                // everyone who tried to join after that got no answer at all and was told to check a
                // port forward that no longer existed.
                Assert.True(PortMapper.RenewSeconds < PortMapper.LeaseSeconds,
                    "renewing after the lease has expired is not renewing");

                // And with room to lose one. A router may hand out a shorter lease than it was asked
                // for, so cutting this fine is asking for the same silence back.
                Assert.True(PortMapper.RenewSeconds <= PortMapper.LeaseSeconds / 2,
                    "renew at half the lease so one lost renewal is survivable");
            });

            TestRunner.Add("net/a NAT-PMP map request is laid out as the RFC says", () =>
            {
                byte[] map = PortMapper.BuildNatPmpRequest(7777, 7200);
                Assert.Equal(map.Length, 12, "twelve bytes");
                Assert.Equal(map[0], 0, "version 0");
                Assert.Equal(map[1], 1, "opcode 1, map UDP");
                Assert.Equal((map[4] << 8) | map[5], 7777, "internal port, big endian");
                Assert.Equal((map[6] << 8) | map[7], 7777, "suggested external port");
                Assert.Equal((int)(((uint)map[8] << 24) | ((uint)map[9] << 16) | ((uint)map[10] << 8) | map[11]),
                    7200, "lease seconds");

                // Zero lease and zero external port is how the same call means "take it away again".
                byte[] remove = PortMapper.BuildNatPmpRequest(7777, 0);
                Assert.Equal((remove[6] << 8) | remove[7], 0, "no external port when removing");
                Assert.Equal(remove[11], 0, "and no lease");
            });
        }
    }
}
