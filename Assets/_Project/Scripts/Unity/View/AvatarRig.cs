using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// An imported character - VRM or glTF - posed from BodyPose.
    ///
    /// THE RULE THIS EXISTS TO ENFORCE: the hitbox does not come from the avatar. Everybody in the
    /// match is the same fifteen capsules, laid over the same skeleton, whatever character they chose
    /// - and the avatar is bent to fit those capsules rather than the capsules being fitted to the
    /// avatar. Two players with different avatars are exactly as hard to hit as each other, and what
    /// you aim at is what the server tests. Press F5 and you can see it.
    ///
    /// That is done in two steps per bone:
    ///
    ///   aim     rotate the bone so it points from its BodyPose joint at its child's BodyPose joint
    ///   fit     scale it along its own length so it REACHES that joint
    ///
    /// The second half is the part that matters and the part most retargeting skips. Without it an
    /// avatar with long arms has hands past the ends of the arm capsules and an avatar with short
    /// ones has hands inside its chest, and in both cases the model disagrees with the hitbox - which
    /// is the exact failure this project has a README rule about.
    ///
    /// Bones are found from the file's DECLARED humanoid map where there is one (VRM has it), and
    /// from node names where there is not. The declared map is right by construction; names are a
    /// guess about what an exporter happened to call things, and exporters disagree.
    /// </summary>
    public sealed class AvatarRig
    {
        /// <summary>One drivable bone: the two BodyPose joints it spans, and its rest length.</summary>
        struct Link
        {
            public Transform Bone;
            public float RestLength;
            public Vector3 RestDirection;   // in the bone's own parent space, at load time
        }

        public GameObject Root { get { return _model != null ? _model.Root : null; } }
        public bool Valid { get { return _hips != null && _model != null; } }

        readonly GlbModel _model;

        Transform _hips, _spine, _chest, _neck, _head;
        Transform _leftShoulder, _leftArm, _leftForearm, _leftHand;
        Transform _rightShoulder, _rightArm, _rightForearm, _rightHand;
        Transform _leftThigh, _leftShin, _leftFoot;
        Transform _rightThigh, _rightShin, _rightFoot;

        Link[] _links;

        public AvatarRig(GlbModel model)
        {
            _model = model;
            if (model == null) return;

            // The DECLARED map first, then names. A file that says which bone is its left upper arm
            // is telling the truth; a file where we matched "LeftArm" is telling us what its exporter
            // happened to call things, and exporters disagree.
            _hips = Bone(model, "hips", "Hips", "mixamorig:Hips", "Armature_Hips", "J_Bip_C_Hips");
            _spine = Bone(model, "spine", "Spine", "mixamorig:Spine", "J_Bip_C_Spine");
            _chest = Bone(model, "upperChest", "Spine2", "Spine1", "mixamorig:Spine2", "J_Bip_C_UpperChest");
            if (_chest == null) _chest = Bone(model, "chest", "Spine1", "J_Bip_C_Chest");
            _neck = Bone(model, "neck", "Neck", "mixamorig:Neck", "J_Bip_C_Neck");
            _head = Bone(model, "head", "Head", "mixamorig:Head", "J_Bip_C_Head");

            _leftShoulder = Bone(model, "leftShoulder", "LeftShoulder", "mixamorig:LeftShoulder", "J_Bip_L_Shoulder");
            _leftArm = Bone(model, "leftUpperArm", "LeftArm", "mixamorig:LeftArm", "J_Bip_L_UpperArm");
            _leftForearm = Bone(model, "leftLowerArm", "LeftForeArm", "mixamorig:LeftForeArm", "J_Bip_L_LowerArm");
            _leftHand = Bone(model, "leftHand", "LeftHand", "mixamorig:LeftHand", "J_Bip_L_Hand");

            _rightShoulder = Bone(model, "rightShoulder", "RightShoulder", "mixamorig:RightShoulder", "J_Bip_R_Shoulder");
            _rightArm = Bone(model, "rightUpperArm", "RightArm", "mixamorig:RightArm", "J_Bip_R_UpperArm");
            _rightForearm = Bone(model, "rightLowerArm", "RightForeArm", "mixamorig:RightForeArm", "J_Bip_R_LowerArm");
            _rightHand = Bone(model, "rightHand", "RightHand", "mixamorig:RightHand", "J_Bip_R_Hand");

            _leftThigh = Bone(model, "leftUpperLeg", "LeftUpLeg", "mixamorig:LeftUpLeg", "J_Bip_L_UpperLeg");
            _leftShin = Bone(model, "leftLowerLeg", "LeftLeg", "mixamorig:LeftLeg", "J_Bip_L_LowerLeg");
            _leftFoot = Bone(model, "leftFoot", "LeftFoot", "mixamorig:LeftFoot", "J_Bip_L_Foot");

            _rightThigh = Bone(model, "rightUpperLeg", "RightUpLeg", "mixamorig:RightUpLeg", "J_Bip_R_UpperLeg");
            _rightShin = Bone(model, "rightLowerLeg", "RightLeg", "mixamorig:RightLeg", "J_Bip_R_LowerLeg");
            _rightFoot = Bone(model, "rightFoot", "RightFoot", "mixamorig:RightFoot", "J_Bip_R_Foot");
        }

        /// <summary>
        /// One bone. The first argument is its STANDARD humanoid name, looked up in whatever map the
        /// file declared; the rest are node names to fall back on when there is no map - Mixamo and
        /// Mixamo style first, then VRoid's J_Bip naming.
        /// </summary>
        static Transform Bone(GlbModel model, string standard, params string[] names)
        {
            Transform declared;
            if (standard != null && model.Humanoid.TryGetValue(standard, out declared) && declared != null)
                return declared;

            for (int i = 0; i < names.Length; i++)
            {
                Transform found = model.Find(names[i]);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Which bones are missing, for the character panel to say so rather than shrug.</summary>
        public string Missing()
        {
            string missing = "";
            if (_hips == null) missing += "Hips ";
            if (_head == null) missing += "Head ";
            if (_leftArm == null) missing += "LeftArm ";
            if (_rightArm == null) missing += "RightArm ";
            if (_leftThigh == null) missing += "LeftUpLeg ";
            if (_rightThigh == null) missing += "RightUpLeg ";
            return missing;
        }

        /// <summary>
        /// Poses the avatar onto a skeleton. `pose` is in character space - origin at the feet, +Z the
        /// way they are facing - and the root transform has already been put in the world, so
        /// everything here is done in local space.
        /// </summary>
        public void Apply(in BodyPose pose, Transform root)
        {
            if (!Valid) return;

            // The hips are placed; everything else is aimed and stretched from them.
            _hips.position = root.TransformPoint(pose.Pelvis.ToUnity());
            _hips.rotation = root.rotation;

            // Spine. The chest is drawn to the shoulder line, same as the blockout character.
            Aim(_spine, root, pose.Pelvis, pose.ChestBase);
            Aim(_chest, root, pose.ChestBase, pose.Shoulders);
            Aim(_neck, root, pose.NeckBase, pose.Head);

            // The head is not aimed at anything - there is no joint above it - so it takes the neck's
            // direction and the aim pitch, which the caller has already baked into pose.Head.
            if (_head != null) _head.rotation = root.rotation * Quaternion.Euler(0f, 0f, 0f);

            Aim(_leftShoulder, root, pose.Shoulders, pose.LeftShoulder);
            Aim(_leftArm, root, pose.LeftShoulder, pose.LeftElbow);
            Aim(_leftForearm, root, pose.LeftElbow, pose.LeftHand);

            Aim(_rightShoulder, root, pose.Shoulders, pose.RightShoulder);
            Aim(_rightArm, root, pose.RightShoulder, pose.RightElbow);
            Aim(_rightForearm, root, pose.RightElbow, pose.RightHand);

            Aim(_leftThigh, root, pose.LeftHip, pose.LeftKnee);
            Aim(_leftShin, root, pose.LeftKnee, pose.LeftAnkle);
            Aim(_leftFoot, root, pose.LeftAnkle, pose.LeftToe);

            Aim(_rightThigh, root, pose.RightHip, pose.RightKnee);
            Aim(_rightShin, root, pose.RightKnee, pose.RightAnkle);
            Aim(_rightFoot, root, pose.RightAnkle, pose.RightToe);
        }

        /// <summary>
        /// Points a bone from one joint at another, and stretches it to reach.
        ///
        /// The rotation is worked out from where the bone's own child actually is once the bone has
        /// been placed, not from a rest direction captured at load - a rest direction goes stale the
        /// moment a parent moves, and the errors compound down a limb until the hands are somewhere
        /// else entirely.
        /// </summary>
        void Aim(Transform bone, Transform root, Vec3 from, Vec3 to)
        {
            if (bone == null) return;

            Vector3 worldFrom = root.TransformPoint(from.ToUnity());
            Vector3 worldTo = root.TransformPoint(to.ToUnity());
            Vector3 delta = worldTo - worldFrom;
            float wanted = delta.magnitude;
            if (wanted < 0.0005f) return;

            bone.position = worldFrom;

            // Where does this bone currently point? At its first child, which is the next joint down
            // the chain. A bone with no children (a toe, a fingertip) keeps its parent's rotation.
            Transform child = bone.childCount > 0 ? bone.GetChild(0) : null;
            if (child == null) return;

            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-8f) return;

            bone.rotation = Quaternion.FromToRotation(current, delta) * bone.rotation;

            // Now it points the right way; make it the right LENGTH. The child sits at a fixed local
            // offset, so scaling the bone along the axis that offset lies on moves the child onto the
            // joint - which is what keeps the avatar inside its own capsules.
            float have = (child.position - bone.position).magnitude;
            if (have < 0.0005f) return;

            float ratio = wanted / have;
            Vector3 local = child.localPosition;
            Vector3 scale = bone.localScale;

            // Whichever local axis the child lies down is the one to stretch.
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az) scale.x *= ratio;
            else if (ay >= az) scale.y *= ratio;
            else scale.z *= ratio;

            // A sanity clamp. An avatar whose proportions are wildly different from the skeleton would
            // otherwise produce a bone stretched by a factor of ten, and a spike across the screen.
            scale = new Vector3(Mathf.Clamp(scale.x, 0.2f, 4f), Mathf.Clamp(scale.y, 0.2f, 4f),
                                Mathf.Clamp(scale.z, 0.2f, 4f));
            bone.localScale = scale;
        }

        public void SetVisible(bool visible)
        {
            if (_model != null && _model.Root != null && _model.Root.activeSelf != visible)
                _model.Root.SetActive(visible);
        }

        public void Destroy()
        {
            if (_model != null && _model.Root != null) Object.Destroy(_model.Root);
        }
    }
}
