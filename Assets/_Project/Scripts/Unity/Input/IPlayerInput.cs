using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// What the game needs from whatever is driving the player, so a phone and a desktop can both
    /// supply it. Both implementations produce an ordinary InputCommand, which is the point: below
    /// this line nothing - prediction, the wire, the server - knows or cares which one it was.
    /// </summary>
    public interface IPlayerInput : IInputSource
    {
        /// <summary>Once per frame, before the tick loop, so aim is as fresh as the display.</summary>
        void PollFrame(float dt, in PlayerSimState predicted);

        /// <summary>False while a panel owns the screen: no firing, no turning.</summary>
        bool Enabled { get; set; }
        bool LookEnabled { get; set; }

        /// <summary>Where the camera is pointing, for the view to follow.</summary>
        void ResetView(float yaw, float pitch);

        /// <summary>Recoil is applied to the aim we send, not just to what is drawn.</summary>
        void ApplyRecoil(int playerId, uint shotIndex, WeaponTuning weapon);

        /// <summary>Is the player asking to sprint - a key on desktop, a stick at its edge on a phone.
        /// The viewmodel wants to know; it has no business asking about keys.</summary>
        bool WantsSprint { get; }

        float SpeedDial { get; }

        /// <summary>
        /// A variable optic's power ring. The game sets ScopeWheel and the limits each frame from
        /// whatever sight is actually fitted, and reads Magnification back to point the scope camera.
        /// It is local and unreplicated: how far you have zoomed changes nothing anyone else can see.
        /// </summary>
        float Magnification { get; set; }
        bool ScopeWheel { get; set; }
        float ScopeMin { get; set; }
        float ScopeMax { get; set; }

        byte CurrentWeapon { get; }
        byte[] Sights { get; }

        FeelTuning Feel { get; set; }
        GameTuning Tuning { get; set; }
    }
}
