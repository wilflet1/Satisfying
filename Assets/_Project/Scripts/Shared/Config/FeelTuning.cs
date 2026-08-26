using System;

namespace Satisfying.Shared
{
    /// <summary>
    /// Purely local presentation values. These never touch the simulation, so every player can tune
    /// their own feel without desyncing anything - unlike MovementTuning, which the host owns.
    /// </summary>
    [Serializable]
    public class FeelTuning
    {
        // ---------------------------------------------------------------- mouse
        [Tune("Mouse", 0.01f, 1.5f, Tip = "Degrees of turn per unit of mouse movement.")]
        public float sensitivity = 0.19f;

        [Tune("Mouse", 0.1f, 2f, Tip = "Sensitivity multiplier while aiming down sights.")]
        public float adsSensitivityMul = 0.72f;

        [Tune("Mouse", 0f, 1f, Tip = "1 inverts vertical aim.")]
        public float invertY = 0f;

        [Tune("Touch", 60f, 900f, Tip = "Degrees turned per inch of thumb travel. Per inch, not per pixel, so a denser screen does not turn faster.")]
        public float touchLookSensitivity = 260f;

        [Tune("Touch", 0.3f, 1f, Tip = "How opaque the on-screen controls are.")]
        public float touchControlAlpha = 0.55f;

        [Tune("Mouse", 0f, 1f, Tip = "Mouse smoothing. 0 is raw and correct; anything else adds latency.")]
        public float smoothing = 0f;

        // ---------------------------------------------------------------- camera
        [Tune("Camera", 60f, 120f)]
        public float fieldOfView = 92f;

        [Tune("Camera", 0.3f, 1f, Tip = "FOV multiplier at full aim.")]
        public float adsFovMul = 0.66f;

        [Tune("Camera", 0f, 25f, Tip = "Extra FOV while sprinting - the speed cue.")]
        public float sprintFovAdd = 7f;

        [Tune("Camera", 0f, 30f, Tip = "Extra FOV while sliding.")]
        public float slideFovAdd = 11f;

        [Tune("Camera", 1f, 30f)]
        public float fovLerpSpeed = 11f;

        [Tune("Camera", 0f, 40f, Tip = "How fast the camera follows the lean.")]
        public float leanSmooth = 17f;

        [Tune("Camera", 0f, 3f, Tip = "Extra camera roll on top of the simulated lean.")]
        public float leanRollExtra = 1f;

        [Tune("Camera", 0f, 6f, Tip = "Camera roll while strafing.")]
        public float strafeRoll = 1.15f;

        [Tune("Camera", 0f, 0.4f, Tip = "How far the camera dips on landing, per metre/second of impact.")]
        public float landDipPerSpeed = 0.016f;

        [Tune("Camera", 0f, 0.6f)]
        public float landDipMax = 0.16f;

        [Tune("Camera", 1f, 40f)]
        public float landRecoverSpeed = 9f;

        // ---------------------------------------------------------------- head bob
        [Tune("Bob", 0f, 20f)]
        public float bobFrequency = 8.6f;

        [Tune("Bob", 0f, 0.15f)]
        public float bobAmplitude = 0.033f;

        [Tune("Bob", 0f, 0.15f)]
        public float bobSideAmount = 0.021f;

        [Tune("Bob", 0f, 3f)]
        public float bobSprintMul = 1.35f;

        [Tune("Bob", 0f, 1f, Tip = "Bob multiplier while aiming.")]
        public float bobAdsMul = 0.25f;

        [Tune("Bob", 0f, 4f, Tip = "Camera roll driven by the bob cycle.")]
        public float bobRoll = 0.55f;

        // ---------------------------------------------------------------- weapon feel
        [Tune("Weapon feel", 0f, 0.2f, Tip = "How far the gun lags behind the camera when you turn.")]
        public float swayPosition = 0.028f;

        [Tune("Weapon feel", 0f, 12f, Tip = "How much the gun rotates when you turn.")]
        public float swayRotation = 4.2f;

        [Tune("Weapon feel", 1f, 40f)]
        public float swaySmooth = 12f;

        [Tune("Weapon feel", 0f, 45f, Tip = "Gun tilt while sprinting.")]
        public float sprintTilt = 24f;

        [Tune("Weapon feel", 0f, 0.3f, Tip = "How far the gun kicks back per shot.")]
        public float recoilKickBack = 0.055f;

        [Tune("Weapon feel", 0f, 2f, Tip = "How much of the weapon recoil is applied to the camera.")]
        public float recoilCameraMul = 1f;

        [Tune("Weapon feel", 1f, 60f)]
        public float recoilStiffness = 26f;

        [Tune("Weapon feel", 1f, 40f)]
        public float recoilDamping = 9f;

        [Tune("Weapon feel", -0.3f, 0.3f, Tip = "Nudge applied on top of the weapon's own hip position.")]
        public float viewmodelX = 0f;

        [Tune("Weapon feel", -0.3f, 0.3f, Tip = "Nudge applied on top of the weapon's own hip position.")]
        public float viewmodelY = 0f;

        [Tune("Weapon feel", -0.3f, 0.3f, Tip = "Nudge applied on top of the weapon's own hip position.")]
        public float viewmodelZ = 0f;

        [Tune("Weapon feel", 0.15f, 0.7f, Tip = "Eye relief: how far in front of your eye the sights sit when aiming.")]
        public float adsSightDistance = 0.4f;

        [Tune("Weapon feel", 35f, 80f, Tip = "Field of view the gun and hands are drawn with. Lower makes the weapon look bigger.")]
        public float viewmodelFov = 58f;

        // The arms are a two bone IK chain reaching for anchors on the gun, so if they are shorter
        // than the distance to the grip they simply stop short and the hands hang in the air behind
        // it - which is not something the viewmodel offsets can fix, because moving the gun moves the
        // target too. These two are the fix: how long the arms are, and where they hang from.
        [Tune("Weapon feel", 0.7f, 1.6f, Tip = "Length of the viewmodel arms. Raise it if the hands do not reach the grip.")]
        public float armLength = 1f;

        [Tune("Weapon feel", -0.35f, 0.25f, Tip = "How far forward the viewmodel shoulders sit. Forward is towards the gun.")]
        public float armForward = 0f;

        [Tune("Weapon feel", 0f, 0.12f, Tip = "How far the gun may travel back towards your eye under recoil, however long you hold the trigger.")]
        public float recoilKickLimit = 0.045f;

        [Tune("Weapon feel", 0f, 14f, Tip = "Degrees the muzzle climbs on the viewmodel per shot. This is the visible lift; it does not move your aim.")]
        public float recoilMuzzleRise = 4.5f;

        [Tune("Weapon feel", 0f, 3f, Tip = "Camera shake per shot, in degrees. Shakes the picture without moving where the round goes.")]
        public float recoilShake = 0.55f;

        [Tune("Weapon feel", 2f, 40f, Tip = "How fast the shake settles.")]
        public float recoilShakeRecovery = 13f;

        // ---------------------------------------------------------------- hud
        [Tune("HUD", 0f, 40f)]
        public float crosshairSize = 7f;

        [Tune("HUD", 0f, 40f)]
        public float crosshairGap = 5f;

        [Tune("HUD", 0f, 6f)]
        public float crosshairThickness = 2f;

        [Tune("HUD", 0f, 1f, Tip = "1 makes the crosshair grow with your actual cone of fire.")]
        public float dynamicCrosshair = 1f;

        [Tune("HUD", 0f, 1f)]
        public float showNetGraph = 0f;

        [Tune("HUD", 0f, 1f, Tip = "Draw the movement state readout.")]
        public float showMovementDebug = 0f;

        // ---------------------------------------------------------------- audio
        [Tune("Audio", 0f, 1f)]
        public float masterVolume = 0.7f;

        [Tune("Audio", 0f, 1.5f, Tip = "How much sound gets through solid geometry. 0 is a perfect wall; 1 is about right for a breeze block one.")]
        public float wallTransmission = 0.55f;

        [Tune("Audio", 0.05f, 3f, Tip = "Metres of material that cut a sound to a third. Thin cover barely muffles; a thick wall kills it.")]
        public float wallHalfDepth = 0.45f;

        [Tune("Audio", 0f, 1f, Tip = "Loss each time a sound has to bend round a corner to reach you.")]
        public float diffractionLoss = 0.35f;

        [Tune("Audio", 0f, 1f, Tip = "1 makes a blocked sound come from the corner it bends round rather than from straight through the wall.")]
        public float diffractionSteering = 1f;

        public FeelTuning Clone() { return (FeelTuning)MemberwiseClone(); }
    }
}
