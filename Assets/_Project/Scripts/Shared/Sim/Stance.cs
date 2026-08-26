namespace Satisfying.Shared
{
    public enum Stance : byte
    {
        Stand = 0,
        Crouch = 1,
        Prone = 2
    }

    /// <summary>Button bits carried by every input command.</summary>
    [System.Flags]
    public enum Buttons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Sprint = 1 << 1,
        Ads = 1 << 2,
        Fire = 1 << 3,
        Reload = 1 << 4,
        SlowLean = 1 << 5,      // modifier: lean eases in at a fraction of the normal rate, and latches
        // Retired: mouse X used to drive an analog lean, which fought the player for control of the view.
        // The slow-lean latch replaced it. The bit stays reserved so an old client cannot repurpose it.
        FreeLean = 1 << 6,
        StepLeft = 1 << 7,
        StepRight = 1 << 8,
        Mantle = 1 << 9,
        Interact = 1 << 10,
        WalkToggle = 1 << 11,   // forces the analog speed dial to its minimum
        BlindFire = 1 << 12,    // gun over/around cover, head stays hidden
        Melee = 1 << 13,        // bash with the stock
        Grab = 1 << 14,         // take hold of a movable object
        // The throw button, still down. The only held button the grenade cares about: press pulls the
        // pin, release throws, and the bit is what tells the simulation which of those just happened.
        Throw = 1 << 15
    }
}
