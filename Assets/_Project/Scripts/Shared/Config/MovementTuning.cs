using System;

namespace Satisfying.Shared
{
    /// <summary>
    /// Every number that affects the authoritative simulation. The host owns these values and
    /// pushes them to clients, because prediction only stays silent when both sides run identical maths.
    /// </summary>
    [Serializable]
    public class MovementTuning
    {
        // ---------------------------------------------------------------- speeds
        [Tune("Speed", 1f, 12f, Tip = "Top speed of a normal walk.")]
        public float walkSpeed = 4.3f;

        [Tune("Speed", 1f, 16f, Tip = "Top speed while sprinting (forward only).")]
        public float sprintSpeed = 7.2f;

        [Tune("Speed", 0.5f, 8f)]
        public float crouchSpeed = 2.15f;

        [Tune("Speed", 0.2f, 4f)]
        public float proneSpeed = 0.95f;

        [Tune("Speed", 0.1f, 1f, Tip = "Speed multiplier while aiming down sights.")]
        public float adsSpeedMul = 0.52f;

        [Tune("Speed", 0.1f, 1f, Tip = "Speed multiplier at full lean.")]
        public float leanSpeedMul = 0.75f;

        [Tune("Speed", 0.2f, 1f, Tip = "Speed multiplier when walking backwards.")]
        public float backwardsSpeedMul = 0.78f;

        [Tune("Speed", 0.2f, 1f, Tip = "Speed multiplier when strafing sideways.")]
        public float strafeSpeedMul = 0.9f;

        [Tune("Speed", 0.05f, 1f, Tip = "Lowest value of the analog speed dial (mouse wheel).")]
        public float speedDialMin = 0.22f;

        [Tune("Speed", 0.02f, 0.5f, Tip = "How much one wheel notch moves the analog speed dial.")]
        public float speedDialStep = 0.12f;

        // ---------------------------------------------------------------- acceleration
        [Tune("Acceleration", 5f, 200f, Tip = "How hard the character is pushed toward the desired velocity on ground.")]
        public float groundAccel = 72f;

        [Tune("Acceleration", 5f, 200f, Tip = "Braking force when there is no move input. Higher = snappier stops.")]
        public float groundFriction = 88f;

        [Tune("Acceleration", 0f, 60f)]
        public float airAccel = 22f;

        [Tune("Acceleration", 0f, 1f, Tip = "How much steering authority you keep in the air.")]
        public float airControl = 0.38f;

        [Tune("Acceleration", 0f, 20f, Tip = "Drag applied to horizontal air velocity.")]
        public float airFriction = 0.6f;

        [Tune("Acceleration", 0f, 1f, Tip = "Extra responsiveness when reversing direction (counter-strafe).")]
        public float counterStrafeBoost = 0.55f;

        // ---------------------------------------------------------------- gravity / jump
        [Tune("Jump", 5f, 45f)]
        public float gravity = 21f;

        [Tune("Jump", 1f, 3f, Tip = "Gravity multiplier while falling - kills the floaty feel.")]
        public float fallGravityMul = 1.55f;

        [Tune("Jump", 1f, 3f, Tip = "Extra gravity when jump is released early (variable jump height).")]
        public float lowJumpGravityMul = 2.1f;

        [Tune("Jump", 0.2f, 2.5f)]
        public float jumpHeight = 1.02f;

        [Tune("Jump", 0f, 0.4f, Tip = "Jump still fires this long after walking off a ledge.")]
        public float coyoteTime = 0.1f;

        [Tune("Jump", 0f, 0.4f, Tip = "Jump pressed this long before landing still fires.")]
        public float jumpBuffer = 0.13f;

        [Tune("Jump", 0f, 1.5f)]
        public float jumpCooldown = 0.28f;

        [Tune("Jump", 0f, 40f, Tip = "Stamina spent per jump.")]
        public float jumpStaminaCost = 9f;

        [Tune("Jump", 0f, 1f, Tip = "How much horizontal speed a landing scrubs off.")]
        public float landSpeedLoss = 0.12f;

        // ---------------------------------------------------------------- collision shape
        [Tune("Body", 1.2f, 2.2f)]
        public float standHeight = 1.82f;

        [Tune("Body", 0.7f, 1.6f)]
        public float crouchHeight = 1.22f;

        [Tune("Body", 0.3f, 1.1f)]
        public float proneHeight = 0.62f;

        [Tune("Body", 0.2f, 0.6f)]
        public float radius = 0.33f;

        [Tune("Body", -0.4f, 0f, Tip = "Eye position measured down from the top of the capsule.")]
        public float eyeDrop = -0.13f;

        [Tune("Body", 0.05f, 0.8f, Tip = "Maximum step height climbed without jumping.")]
        public float stepOffset = 0.42f;

        [Tune("Body", 20f, 70f)]
        public float slopeLimit = 52f;

        [Tune("Body", 0f, 0.8f, Tip = "Downward probe that keeps you glued to slopes and stairs.")]
        public float groundSnapDistance = 0.32f;

        // ---------------------------------------------------------------- stances
        [Tune("Stance", 0.05f, 1f, Tip = "Seconds for stand <-> crouch.")]
        public float crouchTransitionTime = 0.21f;

        [Tune("Stance", 0.1f, 2f, Tip = "Seconds for crouch <-> prone. Committing to prone is deliberately slow.")]
        public float proneTransitionTime = 0.62f;

        [Tune("Stance", 0f, 1f, Tip = "Speed multiplier while a stance change is in progress.")]
        public float stanceChangeSpeedMul = 0.55f;

        [Tune("Stance", 30f, 720f, Tip = "Yaw turn-rate cap while prone (deg/sec).")]
        public float proneYawRateLimit = 150f;

        [Tune("Stance", 0f, 90f, Tip = "Pitch clamp while prone (deg).")]
        public float pronePitchLimit = 55f;

        // ---------------------------------------------------------------- lean
        [Tune("Lean", 0f, 60f, Tip = "Camera/body roll at full lean.")]
        public float leanAngle = 23f;

        [Tune("Lean", 0f, 1.2f, Tip = "Sideways head displacement at full lean - this is what actually peeks the corner.")]
        public float leanOffset = 0.34f;

        [Tune("Lean", 0f, 0.6f, Tip = "Downward head drop at full lean.")]
        public float leanDrop = 0.07f;

        [Tune("Lean", 0.5f, 20f, Tip = "How fast lean builds (units of lean per second).")]
        public float leanSpeed = 6.8f;

        [Tune("Lean", 0.5f, 25f, Tip = "How fast lean returns to centre.")]
        public float leanReturnSpeed = 9.5f;

        [Tune("Lean", 0.05f, 1f, Tip = "Lean speed multiplier while the slow-lean modifier is held.")]
        public float slowLeanSpeedMul = 0.28f;

        [Tune("Lean", 0.001f, 0.06f, Tip = "Mouse travel to lean amount while free-leaning (analog).")]
        public float freeLeanMouseScale = 0.011f;

        [Tune("Lean", 0f, 1.5f, Tip = "Lean multiplier while aiming down sights.")]
        public float adsLeanMul = 0.9f;

        [Tune("Lean", 0f, 1.5f, Tip = "Lean multiplier while prone (a roll rather than a lean).")]
        public float proneLeanMul = 0.55f;

        [Tune("Lean", 0f, 1.5f, Tip = "Lean multiplier while crouched.")]
        public float crouchLeanMul = 1.05f;

        [Tune("Lean", 0f, 30f, Tip = "Stamina drained per second at full lean.")]
        public float leanStaminaDrain = 2.4f;

        [Tune("Lean", 0f, 1f, Tip = "How much a wall next to your head crushes the lean back.")]
        public float leanWallPushback = 1f;

        // ---------------------------------------------------------------- side step
        [Tune("SideStep", 0f, 2.5f, Tip = "Lateral distance of a side step (Alt+A / Alt+D).")]
        public float sideStepDistance = 0.82f;

        [Tune("SideStep", 0.02f, 1.5f, Tip = "Seconds to reach the full side step offset.")]
        public float sideStepTime = 0.17f;

        [Tune("SideStep", 0.02f, 1.5f, Tip = "Seconds to slide back to centre.")]
        public float sideStepReturnTime = 0.22f;

        [Tune("SideStep", 0f, 2f, Tip = "Cooldown before another side step can start.")]
        public float sideStepCooldown = 0.22f;

        [Tune("SideStep", 0f, 40f, Tip = "Stamina spent per side step.")]
        public float sideStepStaminaCost = 6f;

        [Tune("SideStep", 0f, 1f, Tip = "Movement speed multiplier while side stepped.")]
        public float sideStepSpeedMul = 0.85f;

        [Tune("SideStep", 0f, 20f, Tip = "Extra camera roll (deg) at full side step.")]
        public float sideStepRoll = 4.5f;

        // ---------------------------------------------------------------- slide
        [Tune("Slide", 0f, 1f, Tip = "0 disables sprint sliding entirely.")]
        public float slideEnabled = 1f;

        [Tune("Slide", 1f, 12f, Tip = "How fast you must be moving before crouch turns into a slide.")]
        public float slideMinSpeed = 5.2f;

        [Tune("Slide", 1f, 2f, Tip = "Speed multiplier at the moment the slide starts.")]
        public float slideImpulse = 1.25f;

        [Tune("Slide", 0.1f, 3f, Tip = "Maximum length of a slide in seconds.")]
        public float slideDuration = 0.85f;

        [Tune("Slide", 0.5f, 30f, Tip = "How quickly a slide bleeds off speed.")]
        public float slideFriction = 4.2f;

        [Tune("Slide", 0f, 20f, Tip = "How much you can steer mid slide.")]
        public float slideSteering = 5.5f;

        [Tune("Slide", 0f, 40f, Tip = "Extra acceleration when sliding downhill.")]
        public float slideSlopeAccel = 14f;

        [Tune("Slide", 0.5f, 6f, Tip = "Speed below which the slide gives up.")]
        public float slideMinExitSpeed = 2.6f;

        [Tune("Slide", 0f, 3f, Tip = "Cooldown before another slide can start.")]
        public float slideCooldown = 0.55f;

        [Tune("Slide", 0f, 60f)]
        public float slideStaminaCost = 16f;

        [Tune("Slide", 0.3f, 1.4f, Tip = "Capsule height while sliding - low enough to go under things a crouch cannot.")]
        public float slideHeight = 0.78f;

        [Tune("Slide", 1f, 2f, Tip = "Speed kept when you jump out of a slide.")]
        public float slideJumpBoost = 1.08f;

        // ---------------------------------------------------------------- vault
        [Tune("Vault", 0f, 1f, Tip = "0 disables vaulting over thin obstacles.")]
        public float vaultEnabled = 1f;

        [Tune("Vault", 0.2f, 1f, Tip = "Lowest railing worth vaulting rather than stepping over.")]
        public float vaultMinHeight = 0.5f;

        [Tune("Vault", 0.5f, 2f, Tip = "Highest railing you can throw yourself over.")]
        public float vaultMaxHeight = 1.3f;

        [Tune("Vault", 0.2f, 2.5f, Tip = "How far past the railing the game looks for a landing.")]
        public float vaultReachBeyond = 1.15f;

        [Tune("Vault", 0.05f, 1.5f, Tip = "How much lower the far side must be to count as a vault rather than a climb.")]
        public float vaultDropThreshold = 0.35f;

        [Tune("Vault", 0.15f, 1.5f, Tip = "Seconds to get over.")]
        public float vaultTime = 0.46f;

        [Tune("Vault", 0f, 60f)]
        public float vaultStaminaCost = 13f;

        [Tune("Vault", 0f, 10f, Tip = "Forward speed you carry out of a vault.")]
        public float vaultExitSpeed = 3.4f;

        [Tune("Vault", 0.3f, 6f, Tip = "Drop past this and you go over the railing into a fall rather than a landing.")]
        public float vaultMaxDrop = 2.2f;

        // ---------------------------------------------------------------- blind fire
        [Tune("Blind fire", 0.02f, 1f, Tip = "Seconds to raise the weapon over cover.")]
        public float blindFireBlendTime = 0.18f;

        [Tune("Blind fire", 1f, 20f, Tip = "Spread multiplier while blind firing - you cannot see what you are shooting at.")]
        public float blindFireSpreadMul = 7f;

        [Tune("Blind fire", 0f, 1.2f, Tip = "How far the muzzle is lifted above the eye line so you clear the cover.")]
        public float blindFireRaise = 0.42f;

        [Tune("Blind fire", -90f, 0f, Tip = "Lowest weapon elevation on the blind fire dial.")]
        public float blindFirePitchMin = -25f;

        [Tune("Blind fire", 0f, 90f, Tip = "Highest weapon elevation on the blind fire dial.")]
        public float blindFirePitchMax = 45f;

        [Tune("Blind fire", 0f, 90f, Tip = "Sideways swing added when you blind fire while leaning around a corner.")]
        public float blindFireYaw = 32f;

        [Tune("Blind fire", 0.02f, 0.5f, Tip = "How much one wheel notch moves the blind fire dial.")]
        public float blindFireAngleStep = 0.08f;

        [Tune("Blind fire", 0.1f, 1f, Tip = "Movement speed multiplier while blind firing - you can keep walking.")]
        public float blindFireSpeedMul = 0.85f;

        [Tune("Blind fire", 0f, 30f, Tip = "Stamina drained per second while holding the weapon up.")]
        public float blindFireStaminaDrain = 4f;

        // ---------------------------------------------------------------- stamina
        [Tune("Stamina", 10f, 300f)]
        public float staminaMax = 100f;

        [Tune("Stamina", 0f, 60f)]
        public float sprintStaminaDrain = 13f;

        [Tune("Stamina", 0f, 60f)]
        public float adsStaminaDrain = 1.6f;

        [Tune("Stamina", 0f, 120f)]
        public float staminaRegen = 21f;

        [Tune("Stamina", 0f, 4f, Tip = "Delay after spending stamina before it starts regenerating.")]
        public float staminaRegenDelay = 0.75f;

        [Tune("Stamina", 0f, 1f, Tip = "Stamina fraction below which you are winded (cannot sprint, heavy sway).")]
        public float exhaustionThreshold = 0.12f;

        [Tune("Stamina", 0f, 1f, Tip = "Speed multiplier while winded.")]
        public float exhaustedSpeedMul = 0.72f;

        // ---------------------------------------------------------------- mantle
        [Tune("Mantle", 0f, 1f, Tip = "0 disables mantling entirely.")]
        public float mantleEnabled = 1f;

        [Tune("Mantle", 0.1f, 1f)]
        public float mantleMinHeight = 0.45f;

        [Tune("Mantle", 0.5f, 2.5f)]
        public float mantleMaxHeight = 1.35f;

        [Tune("Mantle", 0.2f, 1.5f, Tip = "How far ahead a ledge can be and still be grabbed.")]
        public float mantleReach = 0.85f;

        [Tune("Mantle", 0.1f, 1.5f)]
        public float mantleTime = 0.42f;

        [Tune("Mantle", 0f, 60f)]
        public float mantleStaminaCost = 18f;

        // ---------------------------------------------------------------- aim
        [Tune("Aim", 0.02f, 1f, Tip = "Seconds to fully aim down sights.")]
        public float adsTime = 0.19f;

        [Tune("Aim", 45f, 89.9f)]
        public float pitchLimit = 88f;

        public float JumpVelocity { get { return MathK.Sqrt(2f * MathK.Max(0.01f, gravity) * MathK.Max(0f, jumpHeight)); } }

        public float HeightFor(Stance stance)
        {
            switch (stance)
            {
                case Stance.Crouch: return crouchHeight;
                case Stance.Prone: return proneHeight;
                default: return standHeight;
            }
        }

        public float SpeedFor(Stance stance)
        {
            switch (stance)
            {
                case Stance.Crouch: return crouchSpeed;
                case Stance.Prone: return proneSpeed;
                default: return walkSpeed;
            }
        }

        public MovementTuning Clone()
        {
            return (MovementTuning)MemberwiseClone();
        }
    }
}
