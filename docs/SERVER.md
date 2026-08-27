# Running a server anyone can join

> **If one particular person cannot connect and everyone else can, stop debugging your router.**
> Your port forward is not the variable — a machine on the internet is, and section 3 sets one up
> for free in about twenty minutes. It also removes the host advantage, survives your PC being
> switched off, and cannot be broken by your ISP changing your address.

Three ways, in order of effort.

## 1. Just host, and let the game open the port

Host a duel as normal. While you are hosting, the game asks your router to forward the game port
for you — UPnP first, then NAT-PMP, because routers support one or the other and almost never
both. The menu then shows the address to hand out, with a copy button:

```
  router is forwarding UDP 7777 (UPnP)
  anyone can join at   203.0.113.7:7777        [copy]
```

That is the whole thing. Your friend types that into the address box and clicks join.

The mapping carries a two hour lease, so it expires on its own if the game crashes, and it is
removed when you leave the match.

**If it says the router did not answer**, UPnP is off (many ISP routers ship with it disabled, and
some disable it after a firmware update). Either turn it on — usually under *Advanced → UPnP* — or
forward UDP 7777 to your machine by hand. `-noupnp` on the command line skips the attempt.

### The second line: can anyone actually reach you

UPnP only knows about mappings it made itself, so a port you forwarded by hand on the router looks
exactly like a port that is shut. A separate check asks the outside world instead, and prints its
own line under the UPnP one:

```
  confirmed open - a player reached you from the internet
  UDP 7777 looks forwarded - unconfirmed until someone joins
  your router is remapping UDP 7777 to 51413 - inbound cannot work until that stops
  could not reach a STUN server to check from outside
```

It works two ways. A STUN request goes out of the game socket itself and comes back with the address
and port the internet saw, which is the address to hand out and is known even when UPnP failed. And
any datagram arriving from a public address we never wrote to first is proof the port is open,
because nothing else can produce one.

Only the first line means open. **"Looks forwarded" is deliberately not a yes**: most routers hand
out the same port number even with no forward at all, so a preserved port is consistent with a port
that nobody can reach. It stops being a guess the moment somebody joins. "Remapping", on the other
hand, is a definite no — the router is rewriting your port and no forwarding rule will fix it.

**If you are behind carrier-grade NAT** — common on mobile broadband and some fibre ISPs — no
amount of port forwarding will help, because you do not have a public address to forward from. You
want option 3.

## 2. A machine on your network

Any spare box, including one with no monitor. Build **Satisfying → Build → Linux dedicated server**
(or Windows), copy it over, and run:

```
SatisfyingServer -batchmode -nographics -server -port 7777 -map arena -bots 1
```

A dedicated server has no player of its own: it runs the simulation and answers everyone who turns
up. It prints a line every thirty seconds so a server left running has something in its log.

| Flag | What it does |
|------|--------------|
| `-server` | Run headless with no local player (`-dedicated` is a synonym) |
| `-port N` | UDP port, default 7777 |
| `-map arena` or `-map range` | Which map to serve |
| `-bots N` | Training bots so the server is never empty |
| `-servername "..."` | The name shown to anyone browsing |
| `-noupnp` | Do not ask the gateway to forward anything |

## 3. A free server on the internet

This is the one that works for everybody, from anywhere, without touching a router.

**Oracle Cloud Always Free** is the recommendation: free indefinitely rather than for a trial
period, and it has a Johannesburg region. Any small VPS works the same way.

**Take an AMD (x86-64) shape.** Oracle's headline free machine is an Ampere ARM with 4 cores and
24 GB, but **Satisfying → Build → Linux dedicated server produces an x86-64 binary** and there is no
scripted way to change that here — Unity does ship `linuxarm64` server variations, but
`PlayerSettings.SetArchitecture` accepts an index and quietly changes nothing, so the build says
which architecture it actually produced and it says x86-64. (If you want the ARM machine, set Target
Architecture in Build Settings by hand and check the build line reports AArch64.)

The Always Free tier also includes two **VM.Standard.E2.1.Micro** (AMD, x86-64, 1/8 OCPU, 1 GB), and
one of those is ample. The simulation is about **1% of one core** at 64 Hz — 8461 real server ticks
run in 1.4 seconds of wall clock in `tools/Playground`, with the client and bots in the same
process. A duel is 10 KB/s. Nothing here is short of anything.

### Playing from South Africa

Latency is the whole reason to care where the machine is, and free tiers are thin on the ground in
Africa:

| Provider | South African region | Free? |
|---|---|---|
| **Oracle Cloud** | `af-johannesburg-1` | **Always Free, no time limit** — the one to use |
| AWS | `af-south-1` (Cape Town) | No. Opt-in regions are outside the free tier |
| Azure | South Africa North | Credit for a month, then it bills |
| Google Cloud | `africa-south1` | No. The free `e2-micro` is US-only |

**Pick Johannesburg as your home region when you sign up.** Always Free resources only exist in
your home region, and it cannot be changed afterwards — choosing the default and noticing later
means making a new account. From most of South Africa that is a 5–30 ms ping, against 150 ms+ to
Europe, which is the difference between a duel and a slideshow.

If the free AMD shapes are out of capacity in Johannesburg, keep trying — capacity is released
constantly — or take the cheapest paid shape there, which is a few dollars a month.

1. Create an **Ubuntu 22.04** instance on an **AMD (x86-64)** shape and add your SSH key.
2. Build the server: **Satisfying → Build → Linux dedicated server**.
3. Copy it up and run the deploy script:

```bash
scp -r Builds/LinuxServer ubuntu@YOUR.SERVER.IP:~/satisfying
scp tools/deploy-server.sh ubuntu@YOUR.SERVER.IP:~/
ssh ubuntu@YOUR.SERVER.IP 'bash deploy-server.sh'
```

It writes a systemd unit, opens UDP 7777 in the host firewall, starts the server, sets it to come
back after a reboot, and prints the address to give out. Re-running it upgrades in place.

**Do not skip the cloud provider's own firewall.** It is separate from the one on the machine and
it is where this usually goes wrong:

- Oracle Cloud — VCN → Security Lists → Add Ingress Rule, UDP 7777, source `0.0.0.0/0`
  (Oracle also ships a locked-down iptables inside the machine; the deploy script handles that one.)
- AWS — the instance's security group → Inbound rules
- Hetzner / DigitalOcean — Networking → Firewalls

Then:

```
journalctl -u satisfying -f      # watch it
sudo systemctl restart satisfying
```

## Checking it from outside

The surest test is someone else joining. Failing that, from another network (a phone hotspot
works):

```
nc -u -z -v YOUR.SERVER.IP 7777
```

UDP has no handshake, so a "succeeded" there only means nothing rejected the packet. The server's
own log is the real answer: a join prints a line.

## "Nothing is coming back" when earlier players got in fine

A peer id travels in three bits, so **six players is the hard ceiling** - and the host is one of
them, as it connects to its own server like anybody else. Past that the server answers with a
rejection and the joiner is told the server is full, which is a clear enough answer.

The confusing case is when nobody is told anything. The transport used to hand one of those six
slots to any address that sent it a byte, before anything had looked at what the byte was. A port
forwarded to the open internet receives unsolicited UDP all day, so a few stray datagrams inside the
ten second idle window would fill the table with nothing, and every real player after that was
dropped before the server ever heard about it. They see

> nothing is coming back from ... - check the address, the port forward, and their firewall

and the address, the port forward and the firewall are all fine. A slot is now only spent on a
datagram that is actually asking to join, so noise cannot do this any more.

If it still happens, the host's own screen says so: the hosting panel prints how many players were
turned away for want of a slot. If that line is there, somebody has to leave. If it is not, the
problem really is the network, and the same panel's probe line will say whether the outside world
can reach the port at all.

## It worked for the first couple of hours and then stopped

This one had nothing to do with who was in the match - it happened to a host sitting alone.

When the game opens the port for you it asks the router for a **lease**, so that the forward
disappears by itself if the game crashes rather than being left open forever. That lease was two
hours and nothing renewed it, so after two hours the router quietly took the forward away. The game
kept displaying the success it had had at startup, because the reachability verdict was decided once
and never revisited, so the host had every reason to believe the door was open while nobody could
get in.

The mapping is now renewed at half the lease, and the probe re-checks every five minutes, so a
forward that goes away - a lease expiring, a router rebooting, an ISP handing out a new address -
turns the hosting panel's verdict rather than being reported as a success indefinitely.

**Your public address can change.** If the panel's address is not the one your friend is typing,
that alone explains everything: hand out the new one, or use a dedicated server.

## What it costs to run

A 1v1 duel is about **10 KB/s up and 5 KB/s down per player**, and the simulation is a fixed 64 Hz
tick that does not care how fast the machine is — 8461 server ticks run in 1.4 seconds of wall
clock in `tools/Playground`, client and bots included, which is around 1% of a core at the rate it
actually ticks at. A full server is well under a megabit. Any free
tier will carry it; bandwidth allowances are the only thing worth checking, and a month of solid
play is a few gigabytes.

## What is not here

There is no master server, so there is no list of public servers to browse. On a LAN the host
broadcasts a beacon and appears automatically; over the internet you hand out an address. A
tracker would need somewhere permanent to run, which is a decision about hosting rather than about
the game.

There is no authentication either. Anyone with the address can join, and names are whatever people
type. For a duel with friends that is the point; do not put anything on that server you would mind
a stranger seeing.
