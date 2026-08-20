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

        [Tune("Mouse", 0f, 1f, Tip = "Mouse smoothing. 0 is raw and correct; anything else adds latency.")]
        public float smoothing = 0f;

        // ---------------------------------------------------------------- camera
        [Tune("Camera", 60f, 120f)]
        public float fieldOfView = 92f;

        [Tune("Camera", 0.3f, 1f, Tip = "FOV multiplier at full aim.")]
        public float adsFovMul = 0.66f;

        [Tune("Camera", 0f, 25f, Tip = "Extra FOV while sprinting - the speed cue.")]
        public float sprintFovAdd = 7f;

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

        [Tune("Weapon feel", 0.05f, 0.6f, Tip = "How far in front of your eye the sights sit when aiming.")]
        public float adsSightDistance = 0.22f;

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

        public FeelTuning Clone() { return (FeelTuning)MemberwiseClone(); }
    }
}
