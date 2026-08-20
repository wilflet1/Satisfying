namespace Satisfying.Shared
{
    /// <summary>
    /// One tick of player intent. This is the only thing a client is trusted to send;
    /// the server runs the identical simulation over it and owns the result.
    /// Packed size is 15 bytes, so a 12-command redundant burst still fits comfortably in one datagram.
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
        public Stance StanceRequest;
        public byte WeaponIndex;
        public Buttons Buttons;
        /// <summary>Server tick (fractional, x256) this client was rendering other players at - drives lag compensation.</summary>
        public float RenderTick;

        public bool Has(Buttons b) { return (Buttons & b) != 0; }

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
            b.WriteBits((uint)StanceRequest, 2);
            b.WriteBits(WeaponIndex, 3);
            b.WriteBits((uint)Buttons, 12);
            b.WriteQ(RenderTick - (baseTick - 64f), 0f, 128f, 16);
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
            c.StanceRequest = (Stance)b.ReadBits(2);
            c.WeaponIndex = (byte)b.ReadBits(3);
            c.Buttons = (Buttons)b.ReadBits(12);
            c.RenderTick = b.ReadQ(0f, 128f, 16) + (baseTick - 64f);
            return c;
        }
    }
}
