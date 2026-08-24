namespace Satisfying.Shared
{
    /// <summary>
    /// Turning what someone actually pastes into a host and a port.
    ///
    /// This exists because the address the host is told to hand out - and which the copy button puts
    /// on their clipboard - is "1.2.3.4:7777", while the join box used to accept only a bare address
    /// with the port in a separate field. Pasting the thing you were given failed to resolve, and the
    /// most natural action a player can take was the one that did not work.
    /// </summary>
    public static class NetAddress
    {
        /// <summary>
        /// Pulls a host and port out of free text. Accepts a bare address, "host:port", a stray
        /// scheme, surrounding whitespace, and bracketed IPv6. Returns false only if there is no host
        /// left once all of that is stripped.
        /// </summary>
        public static bool TryParse(string text, int defaultPort, out string host, out int port)
        {
            host = null;
            port = defaultPort;
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.Trim();

            // People paste links. Take what follows the scheme rather than failing on it.
            int scheme = trimmed.IndexOf("://");
            if (scheme >= 0) trimmed = trimmed.Substring(scheme + 3);

            // And trailing paths, which arrive with a pasted link.
            int slash = trimmed.IndexOf('/');
            if (slash >= 0) trimmed = trimmed.Substring(0, slash);

            trimmed = trimmed.Trim();
            if (trimmed.Length == 0) return false;

            // Bracketed IPv6 keeps its colons: [::1]:7777. The socket layer is IPv4 only, so this is
            // about not mangling the input before it gets a clear error rather than about support.
            if (trimmed[0] == '[')
            {
                int close = trimmed.IndexOf(']');
                if (close < 0) return false;
                host = trimmed.Substring(1, close - 1).Trim();
                string afterBracket = trimmed.Substring(close + 1).Trim();
                if (afterBracket.StartsWith(":")) ParsePort(afterBracket.Substring(1), ref port);
                return host.Length > 0;
            }

            int colon = trimmed.LastIndexOf(':');
            if (colon < 0)
            {
                host = trimmed;
                return host.Length > 0;
            }

            // More than one colon and no brackets is a bare IPv6 address, which has no port on it.
            if (trimmed.IndexOf(':') != colon)
            {
                host = trimmed;
                return host.Length > 0;
            }

            host = trimmed.Substring(0, colon).Trim();
            ParsePort(trimmed.Substring(colon + 1), ref port);
            return host.Length > 0;
        }

        /// <summary>A port that is not a sensible number leaves the default alone rather than failing.</summary>
        static void ParsePort(string text, ref int port)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return;

            int parsed = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c < '0' || c > '9') return;
                parsed = parsed * 10 + (c - '0');
                if (parsed > 65535) return;
            }
            if (parsed > 0) port = parsed;
        }
    }
}
