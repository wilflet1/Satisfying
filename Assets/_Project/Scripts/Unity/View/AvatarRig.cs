using System.Collections.Generic;
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
    /// How: every joint the game has is PLACED, and each bone is then turned to look at the next one
    /// down its chain. Placing the joints is what does the work - a bone that starts at one BodyPose
    /// joint and whose child starts at the next spans exactly the right distance by construction, so
    /// no bone ever needs stretching and the skin follows wherever the skeleton goes.
    ///
    /// WHAT THIS USED TO DO, AND WHY IT DOES NOT ANY MORE. The first version scaled each bone along
    /// its own axis to make it reach, and found its child with GetChild(0). That survived a synthetic
    /// test rig, where every bone has exactly one child in the obvious place, and came apart on the
    /// first real character: in a VRM the first child of the chest is as likely to be a hair root or a
    /// skirt bone as the neck, so the "stretch to reach" was computed against a bone two centimetres
    /// long, clamped at four, and multiplied down the chain. Worse, a non-uniform scale on a parent
    /// SHEARS everything below it. The result photographed as a twenty-metre spike with a character
    /// hanging off it. Nothing here touches a bone scale now; the only scale in play is one uniform
    /// factor on the root, which sets how thick the character is and cannot shear anything.
    ///
    /// Bones are found from the file's DECLARED humanoid map where there is one (VRM has it), and
    /// from node names where there is not. The declared map is right by construction; names are a
    /// guess about what an exporter happened to call things, and exporters disagree.
    /// </summary>
    public sealed class AvatarRig
    {
        /// <summary>The height the skeleton is built for. An avatar is scaled to match it.</summary>
        const float ReferenceHeight = 1.82f;

        public GameObject Root { get { return _model != null ? _model.Root : null; } }
        public bool Valid { get { return _hips != null && _model != null; } }

        readonly GlbModel _model;

        Transform _hips, _spine, _chest, _neck, _head;
        Transform _leftShoulder, _leftArm, _leftForearm, _leftHand;
        Transform _rightShoulder, _rightArm, _rightForearm, _rightHand;
        Transform _leftThigh, _leftShin, _leftFoot, _leftToe;
        Transform _rightThigh, _rightShin, _rightFoot, _rightToe;

        float _scale = 1f;
        bool _firstPerson;

        // VRM 0.x characters face the opposite way to VRM 1.0 ones. This is not a bug in either of
        // them: 0.x had the character looking down glTF +Z and 1.0 turned it round to -Z, and the
        // migration notes say so. It survives the handedness conversion as a straight 180 degrees, and
        // without it a 0.x avatar stands with its back to whatever its own rifle is pointing at.
        Quaternion _facing = Quaternion.identity;

        // Where each bone was pointing before anybody posed it. A bone with nothing below it to look
        // at - the head, a hand - is put back to its rest orientation rather than left holding
        // whatever its parent's aim happened to leave it with, which is how you get a head lying on
        // its side because the neck was reaching forward.
        readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();

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

            _leftToe = Bone(model, "leftToes", "LeftToeBase", "mixamorig:LeftToeBase", "J_Bip_L_ToeBase");
            _rightToe = Bone(model, "rightToes", "RightToeBase", "mixamorig:RightToeBase", "J_Bip_R_ToeBase");

            if (model.Flavour == "VRM 0.x") _facing = Quaternion.Euler(0f, 180f, 0f);

            Normalise();
            RememberRest();
        }

        /// <summary>
        /// One uniform scale on the root, so a character built at some other height is the size of
        /// everybody else.
        ///
        /// This does not change where any joint ends up - those are placed - only how THICK the
        /// character is and how big its head is, which is exactly what wants matching: the capsules
        /// are sized for a 1.82 m duellist, and an avatar authored at 1.5 m rattles around inside them
        /// while one authored at 2 m has shoulders sticking out of the sides of its own hitbox.
        /// </summary>
        void Normalise()
        {
            if (_model == null || _model.Root == null || _hips == null) return;

            Transform root = _model.Root.transform;
            root.localScale = Vector3.one;

            // Measure the skeleton rather than the mesh: hair, a hat or a held prop all move a
            // renderer bound and none of them are how tall somebody is.
            float low = float.MaxValue, high = float.MinValue;
            foreach (KeyValuePair<string, Transform> kv in _model.Bones)
            {
                if (kv.Value == null) continue;
                float y = root.InverseTransformPoint(kv.Value.position).y;
                if (y < low) low = y;
                if (y > high) high = y;
            }
            if (high <= low) return;

            // The topmost bone is the crown of the skull at best, and usually somewhere inside it, so
            // the skeleton is a little shorter than the character. Eight centimetres of scalp is about
            // right and stops every avatar coming out fractionally too large.
            float height = (high - low) + 0.08f;
            if (height < 0.5f || height > 4f) return;

            _scale = Mathf.Clamp(ReferenceHeight / height, 0.5f, 2f);
            root.localScale = new Vector3(_scale, _scale, _scale);
        }

        void RememberRest()
        {
            if (_model == null || _model.Root == null) return;
            Quaternion inverse = Quaternion.Inverse(_model.Root.transform.rotation);
            foreach (KeyValuePair<string, Transform> kv in _model.Bones)
                if (kv.Value != null) _rest[kv.Value] = inverse * kv.Value.rotation;
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
        /// way they are facing - and the root transform has already been put in the world.
        ///
        /// Order matters: a bone is placed only after its parent has been turned, so it is worked
        /// from the hips outwards and each limb from the shoulder or hip down.
        /// </summary>
        public void Apply(in BodyPose pose, Transform root)
        {
            if (!Valid) return;

            _hips.position = root.TransformPoint(pose.Pelvis.ToUnity());
            _hips.rotation = root.rotation * _facing * Rest(_hips);

            // Spine. The chest is drawn up to the shoulder line, the neck to the head joint.
            Segment(_spine, Below(_chest, _neck, _head), root, pose.Pelvis, pose.ChestBase);
            Segment(_chest, Below(_neck, _head), root, pose.ChestBase, pose.Shoulders);
            Segment(_neck, _head, root, pose.NeckBase, pose.Head);

            // Nothing sits above the head to aim it at, so it goes back to rest and faces the way the
            // body does. Where the player is LOOKING is a camera concern and deliberately not this
            // one - a remote duellist's head does not swivel, and their aim is read off their weapon.
            if (_head != null && !_firstPerson)
            {
                _head.position = root.TransformPoint(pose.Head.ToUnity());
                _head.rotation = root.rotation * _facing * Rest(_head);
            }

            // Your own arms are the viewmodel's job, and the bones have been collapsed to nothing.
            // Placing a joint through a bone scaled to a ten-thousandth asks for a local offset ten
            // thousand times too big, which is how you get a limb across the whole map.
            if (!_firstPerson)
            {
                Segment(_leftShoulder, Below(_leftArm, _leftForearm), root, pose.Shoulders, pose.LeftShoulder);
                Segment(_leftArm, Below(_leftForearm, _leftHand), root, pose.LeftShoulder, pose.LeftElbow);
                Segment(_leftForearm, _leftHand, root, pose.LeftElbow, pose.LeftHand);
                Settle(_leftHand, root, pose.LeftHand, _leftForearm);

                Segment(_rightShoulder, Below(_rightArm, _rightForearm), root, pose.Shoulders, pose.RightShoulder);
                Segment(_rightArm, Below(_rightForearm, _rightHand), root, pose.RightShoulder, pose.RightElbow);
                Segment(_rightForearm, _rightHand, root, pose.RightElbow, pose.RightHand);
                Settle(_rightHand, root, pose.RightHand, _rightForearm);
            }

            Segment(_leftThigh, Below(_leftShin, _leftFoot), root, pose.LeftHip, pose.LeftKnee);
            Segment(_leftShin, Below(_leftFoot, _leftToe), root, pose.LeftKnee, pose.LeftAnkle);
            Segment(_leftFoot, _leftToe, root, pose.LeftAnkle, pose.LeftToe);

            Segment(_rightThigh, Below(_rightShin, _rightFoot), root, pose.RightHip, pose.RightKnee);
            Segment(_rightShin, Below(_rightFoot, _rightToe), root, pose.RightKnee, pose.RightAnkle);
            Segment(_rightFoot, _rightToe, root, pose.RightAnkle, pose.RightToe);
        }

        /// <summary>
        /// Puts a bone on its joint and turns it to look at the next one down the chain.
        ///
        /// The direction it is CURRENTLY pointing is measured to a named bone, never to whatever
        /// happens to be its first child. On a real character the first child of the chest is as
        /// likely to be a hair root as the neck, and aiming a two-centimetre hair bone at the shoulder
        /// line is how the whole skeleton used to come apart.
        /// </summary>
        void Segment(Transform bone, Transform next, Transform root, Vec3 from, Vec3 to)
        {
            if (bone == null) return;

            bone.position = root.TransformPoint(from.ToUnity());
            if (next == null) return;

            Vector3 wanted = root.TransformPoint(to.ToUnity()) - bone.position;
            Vector3 have = next.position - bone.position;
            if (wanted.sqrMagnitude < 1e-8f || have.sqrMagnitude < 1e-8f) return;

            bone.rotation = Quaternion.FromToRotation(have, wanted) * bone.rotation;
        }

        /// <summary>A hand or a foot: on its joint, and carried round by the limb above it.</summary>
        void Settle(Transform bone, Transform root, Vec3 at, Transform above)
        {
            if (bone == null) return;
            bone.position = root.TransformPoint(at.ToUnity());
            if (above != null) bone.rotation = above.rotation * Quaternion.Inverse(Rest(above)) * Rest(bone);
        }

        /// <summary>The first of these bones that the file actually has.</summary>
        static Transform Below(params Transform[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null) return candidates[i];
            return null;
        }

        Quaternion Rest(Transform bone)
        {
            Quaternion rest;
            if (bone != null && _rest.TryGetValue(bone, out rest)) return rest;
            return Quaternion.identity;
        }

        /// <summary>
        /// Wears this character as your OWN body - the one you see when you look down.
        ///
        /// The head and the arms come off. The camera lives inside the skull, so a head left on fills
        /// the screen with the inside of somebody's face, and the arms are already being drawn at
        /// arm's length by the viewmodel: two sets of arms is worse than none. That is the same thing
        /// the blockout duellist does, and the reason your own body has never been a character until
        /// now is that this was missing and the avatar was simply switched off instead.
        ///
        /// "Comes off" means collapsed to a point, not hidden, because a skinned character is one mesh
        /// and there is nothing to hide separately - shrinking the bone takes its vertices with it.
        /// This is the one place a bone scale is allowed. It is safe where the rest of this class
        /// refuses to touch one, because nothing below these bones is ever aimed at anything
        /// afterwards: Apply skips the whole arm chain and the head while it is on.
        /// </summary>
        public void SetFirstPerson(bool first)
        {
            _firstPerson = first;

            float scale = first ? 0.0001f : 1f;
            Collapse(_head, scale);
            Collapse(_leftShoulder, scale);
            Collapse(_rightShoulder, scale);
            if (_leftShoulder == null) Collapse(_leftArm, scale);
            if (_rightShoulder == null) Collapse(_rightArm, scale);
        }

        static void Collapse(Transform bone, float scale)
        {
            if (bone != null) bone.localScale = new Vector3(scale, scale, scale);
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
