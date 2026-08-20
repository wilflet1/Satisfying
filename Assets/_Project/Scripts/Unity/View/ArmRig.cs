using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// A two bone arm with an analytic IK solver. The hands are pinned to anchors on the weapon, so the
    /// arms follow the gun automatically - through sway, recoil, aiming and the reload - instead of
    /// needing a separate animation per weapon.
    /// </summary>
    public sealed class ArmRig
    {
        public Transform Shoulder;
        public Transform UpperArm;
        public Transform Elbow;
        public Transform Forearm;
        public Transform Hand;

        public float UpperLength = 0.27f;
        public float ForearmLength = 0.26f;
        public float Side = 1f;                 // +1 right arm, -1 left arm

        public static ArmRig Build(Transform parent, string name, Vector3 shoulderPosition, float side,
                                   Palette palette, Material skin, int layer, float thickness = 0.075f,
                                   float upperLength = 0.27f, float forearmLength = 0.26f)
        {
            ArmRig arm = new ArmRig();
            arm.Side = side;
            arm.UpperLength = upperLength;
            arm.ForearmLength = forearmLength;

            GameObject shoulder = new GameObject(name);
            shoulder.layer = layer;
            shoulder.transform.SetParent(parent, false);
            shoulder.transform.localPosition = shoulderPosition;
            arm.Shoulder = shoulder.transform;

            GameObject upper = new GameObject("upper arm");
            upper.layer = layer;
            upper.transform.SetParent(arm.Shoulder, false);
            arm.UpperArm = upper.transform;
            Blockout.Box(arm.UpperArm, "upper mesh", new Vector3(0f, 0f, upperLength * 0.5f),
                new Vector3(thickness, thickness, upperLength), skin, false, layer);

            GameObject elbow = new GameObject("elbow");
            elbow.layer = layer;
            elbow.transform.SetParent(arm.UpperArm, false);
            elbow.transform.localPosition = new Vector3(0f, 0f, upperLength);
            arm.Elbow = elbow.transform;

            GameObject forearm = new GameObject("forearm");
            forearm.layer = layer;
            forearm.transform.SetParent(arm.Elbow, false);
            arm.Forearm = forearm.transform;
            Blockout.Box(arm.Forearm, "forearm mesh", new Vector3(0f, 0f, forearmLength * 0.5f),
                new Vector3(thickness * 0.88f, thickness * 0.88f, forearmLength), skin, false, layer);

            GameObject hand = new GameObject("hand");
            hand.layer = layer;
            hand.transform.SetParent(arm.Forearm, false);
            hand.transform.localPosition = new Vector3(0f, 0f, forearmLength);
            arm.Hand = hand.transform;
            Blockout.Box(arm.Hand, "palm", new Vector3(0f, 0f, 0.035f), new Vector3(0.062f, 0.038f, 0.085f), skin, false, layer);
            Blockout.Box(arm.Hand, "thumb", new Vector3(-0.030f * side, 0.014f, 0.030f), new Vector3(0.024f, 0.024f, 0.050f), skin, false, layer);

            return arm;
        }

        public void SetVisible(bool visible)
        {
            if (Shoulder != null && Shoulder.gameObject.activeSelf != visible) Shoulder.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Law-of-cosines IK. The pole direction decides which way the elbow breaks; for a human arm
        /// that is down and slightly outward, which is what keeps it from inverting behind the gun.
        /// </summary>
        public void Solve(Vector3 worldTarget, Vector3 worldPole, Quaternion handRotation)
        {
            Vector3 root = Shoulder.position;
            Vector3 toTarget = worldTarget - root;
            float reach = UpperLength + ForearmLength;
            float distance = Mathf.Clamp(toTarget.magnitude, 0.02f, reach - 0.004f);
            if (toTarget.sqrMagnitude < 1e-6f) return;

            Vector3 direction = toTarget.normalized;
            if (worldPole.sqrMagnitude < 1e-6f) worldPole = Vector3.down;

            // Keep the pole perpendicular enough that LookRotation stays stable.
            Vector3 pole = Vector3.ProjectOnPlane(worldPole, direction);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.down, direction);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.right, direction);

            float shoulderAngle = Mathf.Acos(Mathf.Clamp(
                (UpperLength * UpperLength + distance * distance - ForearmLength * ForearmLength) /
                (2f * UpperLength * distance), -1f, 1f)) * Mathf.Rad2Deg;

            float elbowAngle = Mathf.Acos(Mathf.Clamp(
                (UpperLength * UpperLength + ForearmLength * ForearmLength - distance * distance) /
                (2f * UpperLength * ForearmLength), -1f, 1f)) * Mathf.Rad2Deg;

            Quaternion aim = Quaternion.LookRotation(direction, pole.normalized);
            UpperArm.rotation = aim * Quaternion.Euler(-shoulderAngle, 0f, 0f);
            Forearm.localRotation = Quaternion.Euler(180f - elbowAngle, 0f, 0f);
            Hand.rotation = handRotation;
        }
    }
}
