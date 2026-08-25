using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Checks the rule Art/README.md states and nothing has ever enforced: nothing drawn on a duellist
    /// may stick out past the capsules PlayerHitbox tests. A helmet that overhangs the head sphere is a
    /// helmet you can put a round through for no damage, and from the shooting end that is
    /// indistinguishable from broken netcode.
    ///
    /// Every vertex of every mesh on the body is measured against all fifteen capsules, in every stance.
    /// The weapon is not part of the body and is not tested - you cannot shoot someone's rifle.
    /// </summary>
    public static class Silhouette
    {
        /// <summary>The shoulders are the one documented exception: the chest capsule is narrower than
        /// the drawn shoulders because the arm capsules start at the shoulder joints and cover it.</summary>
        const float Tolerance = 0.002f;

        struct Row { public float Excess; public Vec3 Point; public HitZone Zone; public string Label; }

        static Dictionary<string, Row> _worst;

        [MenuItem("Satisfying/Shots/Check the silhouette", priority = 62)]
        public static void Run()
        {
            Palette palette = Palette.Build();
            MovementTuning move = new MovementTuning();
            WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();

            _worst = new Dictionary<string, Row>();
            StringBuilder report = new StringBuilder();
            int worstCount = 0;

            for (int w = 0; w < weapons.Length; w++)
            {
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " stand", State(move, Stance.Stand, 0f));
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " stand ads", State(move, Stance.Stand, 1f));
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " crouch", State(move, Stance.Crouch, 0f));
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " crouch ads", State(move, Stance.Crouch, 1f));
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " prone", State(move, Stance.Prone, 0f));
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " prone ads", State(move, Stance.Prone, 1f));

                PlayerSimState slide = State(move, Stance.Crouch, 0f);
                slide.Sliding = true;
                slide.Velocity = new Vec3(0f, 0f, 7.2f);
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " slide", slide);

                PlayerSimState vault = State(move, Stance.Stand, 0f);
                vault.Vaulting = true;
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " vault", vault);

                PlayerSimState lean = State(move, Stance.Stand, 0f);
                lean.Lean = 1f;
                worstCount += Check(report, palette, move, weapons[w], "w" + w + " lean", lean);
            }

            if (_worst.Count == 0)
            {
                Debug.Log("[silhouette] every drawn vertex is inside a capsule.");
                return;
            }

            StringBuilder summary = new StringBuilder();
            summary.AppendLine("[silhouette] " + _worst.Count + " parts stick out past the hitbox (worst over every stance):");
            foreach (KeyValuePair<string, Row> pair in _worst)
                summary.AppendLine("  " + pair.Key.PadRight(22) +
                                   (pair.Value.Excess * 1000f).ToString("0").PadLeft(4) + " mm past the " +
                                   pair.Value.Zone.ToString().ToLowerInvariant().PadRight(8) +
                                   " at (" + pair.Value.Point.x.ToString("0.000") + ", " +
                                   pair.Value.Point.y.ToString("0.000") + ", " +
                                   pair.Value.Point.z.ToString("0.000") + ")  " + pair.Value.Label);
            Debug.Log(summary.ToString());
        }

        static PlayerSimState State(MovementTuning move, Stance stance, float ads)
        {
            PlayerSimState s = new PlayerSimState();
            s.Height = move.HeightFor(stance);
            s.Stance = stance;
            s.Grounded = true;
            s.Ads = ads;
            s.Stamina = move.staminaMax;
            s.Weapon.Ammo = 30;
            return s;
        }

        static int Check(StringBuilder report, Palette palette, MovementTuning move, WeaponTuning weapon,
                         string label, PlayerSimState state)
        {
            GameObject holder = new GameObject("silhouette subject");
            PlayerNetState net = PlayerNetState.FromSim(0, in state, true, 100f);
            RemotePlayerView view = new RemotePlayerView(holder.transform, 1, palette, move, GameBootstrap.LayerPlayer);
            float impulse;
            view.Render(in net, 1f / 64f, weapon, out impulse);

            PlayerHitbox box = PlayerHitbox.FromState(in state, move, weapon);

            Dictionary<string, float> worst = new Dictionary<string, float>();
            MeshFilter[] filters = view.Character.Root.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < filters.Length; i++)
            {
                if (IsWeapon(filters[i].transform)) continue;
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;

                Matrix4x4 toWorld = filters[i].transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                float excess = -1f;
                Vec3 at = new Vec3();
                HitZone nearest = HitZone.None;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vec3 point = toWorld.MultiplyPoint3x4(vertices[v]).ToSim();
                    HitZone zone;
                    float d = Outside(point, in box, out zone);
                    if (d <= excess) continue;
                    excess = d;
                    at = point;
                    nearest = zone;
                }
                if (excess <= Tolerance) continue;

                string part = Name(filters[i].transform);
                worst[part] = excess;
                Row row;
                if (!_worst.TryGetValue(part, out row) || excess > row.Excess)
                {
                    row.Excess = excess;
                    row.Point = at;
                    row.Zone = nearest;
                    row.Label = label;
                    _worst[part] = row;
                }
            }

            Object.DestroyImmediate(holder);

            if (worst.Count == 0) return 0;
            foreach (KeyValuePair<string, float> pair in worst)
                report.AppendLine("  " + label.PadRight(14) + pair.Key.PadRight(28) +
                                  (pair.Value * 1000f).ToString("0") + " mm outside");
            return worst.Count;
        }

        static bool IsWeapon(Transform t)
        {
            while (t != null)
            {
                if (t.name == "weapon holder") return true;
                t = t.parent;
            }
            return false;
        }

        static string Name(Transform t)
        {
            string name = t.name;
            if (name.EndsWith(" mesh") && t.parent != null) name = t.parent.name;
            return name;
        }

        /// <summary>How far outside the nearest capsule this point is. Negative means inside one.</summary>
        static float Outside(Vec3 point, in PlayerHitbox box, out HitZone nearest)
        {
            nearest = HitZone.None;
            float best = float.MaxValue;
            for (int i = 0; i < PlayerHitbox.SegmentCount; i++)
            {
                Vec3 a, b;
                float radius;
                HitZone zone;
                box.Segment(i, out a, out b, out radius, out zone);

                Vec3 ab = b - a;
                float length2 = ab.SqrMagnitude;
                float t = length2 < 1e-8f ? 0f : MathK.Clamp01(Vec3.Dot(point - a, ab) / length2);
                float distance = (point - (a + ab * t)).Magnitude - radius;
                if (distance >= best) continue;
                best = distance;
                nearest = zone;
            }
            return best;
        }
    }
}
