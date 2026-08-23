namespace Satisfying.Shared
{
    /// <summary>
    /// One tick of player intent. This is the only thing a client is trusted to send;
    /// the server runs the identical simulation over it and owns the result.
    /// Packed size is 18 bytes, so a 12-command redundant burst still fits comfortably in one datagram.
    /// </summary>
    public struct InputCommand
    {
        public uint Tick;
        public float MoveX;         // -1..1 strafe
        public float MoveY;         // -1..1 forward
        public float Yaw;           // absolute degrees
        public float Pitch;         // absolute degrees, negative = up
        public float LeanAxis;      // -1..1 desired lean (analog when free-leaning)
        public float SpeedDial;     // 0..1 analog walk speed
        public float BlindAngle;    // -1..1 blind fire elevation dial (mouse wheel while blind firing)
        public Stance StanceRequest;
        public byte WeaponIndex;
        public byte SightIndex;     // optic fitted to the weapon in hand
        public Buttons Buttons;

        // Press counters, not button edges. A button edge is only visible if the exact tick it
        // happened on arrives: lose that packet and the server never sees the press, or repeats the
        // held state and never sees the release. A counter rides in every later command too, so the
        // press survives loss, and a repeated tick cannot invent one.
        public byte GrabSeq;
        public byte MeleeSeq;
        /// <summary>Server tick (fractional, x256) this client was rendering other players at - drives lag compensation.</summary>
        public float RenderTick;

        public bool Has(Buttons b) { return (Buttons & b) != 0; }

        public void PressGrab()  { GrabSeq  = (byte)((GrabSeq  + 1) & 7); Buttons |= Buttons.Grab; }
        public void PressMelee() { MeleeSeq = (byte)((MeleeSeq + 1) & 7); Buttons |= Buttons.Melee; }

        /// <summary>
        /// True when the counter has moved forward, in the wrapping sense. Half the space counts as
        /// ahead and half as behind, so a stale or duplicated packet is ignored rather than read as a
        /// press - you would have to hit the key four times inside one round trip to fool it.
        /// </summary>
        public static bool Advanced(byte incoming, byte seen)
        {
            int delta = (incoming - seen) & 7;
            return delta > 0 && delta < 4;
        }

        public static InputCommand Default(uint tick)
        {
            InputCommand c = new InputCommand();
            c.Tick = tick;
            c.SpeedDial = 1f;
            c.StanceRequest = Stance.Stand;
            return c;
        }

        /// <summary>Repeats the previous intent on a dropped tick - far better than a stall.</summary>
        public InputCommand Repeat(uint tick)
        {
            InputCommand c = this;
            c.Tick = tick;
            c.Buttons &= ~(Buttons.Jump | Buttons.Mantle | Buttons.StepLeft | Buttons.StepRight | Buttons.Interact);
            return c;
        }

        public void Write(NetBuffer b, uint baseTick)
        {
            b.WriteBits(baseTick - Tick, 6);            // 0..63 ticks behind the burst head
            b.WriteQ(MoveX, -1f, 1f, 8);
            b.WriteQ(MoveY, -1f, 1f, 8);
            b.WriteQ(MathK.Repeat(Yaw, 360f), 0f, 360f, 16);
            b.WriteQ(MathK.Clamp(Pitch, -90f, 90f), -90f, 90f, 16);
            b.WriteQ(LeanAxis, -1f, 1f, 8);
            b.WriteQ(SpeedDial, 0f, 1f, 6);
            b.WriteQ(BlindAngle, -1f, 1f, 7);
            b.WriteBits((uint)StanceRequest, 2);
            b.WriteBits(WeaponIndex, 3);
            b.WriteBits(SightIndex, 2);
            b.WriteBits((uint)Buttons, 16);
            b.WriteBits(GrabSeq, 3);
            b.WriteBits(MeleeSeq, 3);
            b.WriteQ(RenderTick - (baseTick - 64f), 0f, 128f, 12);
        }

        public static InputCommand Read(NetBuffer b, uint baseTick)
        {
            InputCommand c = new InputCommand();
            c.Tick = baseTick - b.ReadBits(6);
            c.MoveX = b.ReadQ(-1f, 1f, 8);
            c.MoveY = b.ReadQ(-1f, 1f, 8);
            c.Yaw = b.ReadQ(0f, 360f, 16);
            c.Pitch = b.ReadQ(-90f, 90f, 16);
            c.LeanAxis = b.ReadQ(-1f, 1f, 8);
            c.SpeedDial = b.ReadQ(0f, 1f, 6);
            c.BlindAngle = b.ReadQ(-1f, 1f, 7);
            c.StanceRequest = (Stance)b.ReadBits(2);
            c.WeaponIndex = (byte)b.ReadBits(3);
            c.SightIndex = (byte)b.ReadBits(2);
            c.Buttons = (Buttons)b.ReadBits(16);
            c.GrabSeq = (byte)b.ReadBits(3);
            c.MeleeSeq = (byte)b.ReadBits(3);
            c.RenderTick = b.ReadQ(0f, 128f, 12) + (baseTick - 64f);
            return c;
        }
    }
}
