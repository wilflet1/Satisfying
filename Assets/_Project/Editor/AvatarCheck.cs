using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using Satisfying.Shared;
using UnityEngine;

namespace Satisfying.Game
{
    /// <summary>
    /// Puts a GLB through the loader and reports what came out, without needing the game running.
    ///
    /// A model loader is exactly the kind of thing that compiles perfectly and produces a bag of
    /// confetti, and there is no way to tell which from the source. Point this at a .glb - one in the
    /// avatar cache, or any file you pass with -avatar - and it says how many meshes, how many
    /// vertices, whether it is skinned, which of the bones the game needs are present, and how big it
    /// is. Then it photographs it.
    /// </summary>
    public static class AvatarCheck
    {
        /// <summary>
        /// The deal: which peer gets which character, and whether it is stable and spread. Both halves
        /// matter - stable because two machines have to agree without talking, spread because a deal
        /// that hands four of six players the same character is not a deal.
        /// </summary>
        [MenuItem("Satisfying/Shots/Check the character deal", priority = 67)]
        public static void CheckDeal()
        {
            AvatarPool pool = new AvatarPool(null);
            StringBuilder report = new StringBuilder();
            report.AppendLine("[deal] blockout variants by peer id, and how they spread");

            int[] kitCounts = new int[8];
            for (int peer = 1; peer <= 6; peer++)
            {
                int variant = pool.VariantFor(peer);
                int again = pool.VariantFor(peer);
                report.AppendLine("  peer " + peer + "   variant " + variant
                                  + "   kit " + (Mathf.Abs(variant) % 8)
                                  + "   skin " + (Mathf.Abs(variant / 8) % 6)
                                  + (variant == again ? "" : "   UNSTABLE"));
                kitCounts[Mathf.Abs(variant) % 8]++;
            }

            int distinct = 0;
            for (int i = 0; i < kitCounts.Length; i++) if (kitCounts[i] > 0) distinct++;
            report.AppendLine("  " + distinct + " distinct kits across 6 players");
            Debug.Log(report.ToString());
        }

        [MenuItem("Satisfying/Shots/Check an avatar", priority = 66)]
        public static void Run()
        {
            string path = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-avatar") path = args[i + 1];

            if (string.IsNullOrEmpty(path))
            {
                string folder = Path.Combine(Application.persistentDataPath, "avatars");
                if (Directory.Exists(folder))
                {
                    string[] found = Directory.GetFiles(folder, "*.glb");
                    if (found.Length > 0) path = found[0];
                }
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.Log("[avatar] no .glb to check. Pass one with -avatar <path>, or put one in "
                          + Path.Combine(Application.persistentDataPath, "avatars"));
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Debug.Log("[avatar] " + Path.GetFileName(path) + "  " + (bytes.Length / 1024) + " KB");

            string error;
            GlbModel model = GlbLoader.Load(bytes, "avatar", Shader.Find("Standard"), out error);
            if (model == null)
            {
                Debug.LogError("[avatar] FAILED: " + error);
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine("[avatar] loaded");

            int vertices = 0;
            int triangles = 0;
            MeshFilter[] filters = model.Root.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skins = model.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null) continue;
                vertices += filters[i].sharedMesh.vertexCount;
                triangles += filters[i].sharedMesh.triangles.Length / 3;
            }
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i].sharedMesh == null) continue;
                vertices += skins[i].sharedMesh.vertexCount;
                triangles += skins[i].sharedMesh.triangles.Length / 3;
            }

            report.AppendLine("  meshes      " + (filters.Length + skins.Length)
                              + " (" + skins.Length + " skinned)");
            report.AppendLine("  vertices    " + vertices);
            report.AppendLine("  triangles   " + triangles);
            report.AppendLine("  bones       " + model.Bones.Count);
            report.AppendLine("  format      " + model.Flavour
                              + (model.Humanoid.Count > 0
                                 ? "  (declared humanoid map, " + model.Humanoid.Count + " bones)"
                                 : "  (no humanoid map - bones matched by name)"));

            AvatarRig rig = new AvatarRig(model);
            string missing = rig.Missing();
            report.AppendLine("  rig         " + (missing.Length == 0
                ? "every bone the game drives is present"
                : "MISSING " + missing));

            // How big is it, really? An avatar that loads at the wrong scale poses correctly and looks
            // absurd, and the number is the fastest way to see that.
            Bounds bounds = new Bounds(model.Root.transform.position, Vector3.zero);
            bool any = false;
            Renderer[] renderers = model.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!any) { bounds = renderers[i].bounds; any = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            if (any)
                report.AppendLine("  size        " + bounds.size.x.ToString("0.00") + " x "
                                  + bounds.size.y.ToString("0.00") + " x " + bounds.size.z.ToString("0.00") + " m");

            Debug.Log(report.ToString());

            // ---- and now the thing that actually matters: does it land inside the capsules?
            //
            // The avatar is posed from BodyPose and the hitbox is built from BodyPose, so they should
            // agree by construction - but "should" is what this project has a rule about. The overlay
            // is drawn over the posed avatar and photographed, and if a limb is outside its capsule
            // you can see it rather than find out from a player.
            PoseShot(model, path);
            Object.DestroyImmediate(model.Root);
        }

        static void PoseShot(GlbModel model, string path)
        {
            string outDir = "Screenshots";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shotsOut") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);

            MovementTuning move = new MovementTuning();
            WeaponTuning weapon = WeaponTuning.DefaultLoadout()[0];

            GameObject holder = new GameObject("posed avatar");
            model.Root.transform.SetParent(holder.transform, false);
            model.Root.SetActive(true);

            AvatarRig rig = new AvatarRig(model);
            HitboxView boxes = new HitboxView(holder.transform, 0);
            boxes.SetVisible(true);

            Stance[] stances = { Stance.Stand, Stance.Crouch, Stance.Prone };
            string[] names = { "stand", "crouch", "prone" };

            for (int i = 0; i < stances.Length; i++)
            {
                PlayerSimState state = new PlayerSimState();
                state.Height = move.HeightFor(stances[i]);
                state.Stance = stances[i];
                state.Grounded = true;
                state.Stamina = move.staminaMax;
                state.Weapon.Ammo = 30;

                BodyPose pose = BodyPose.Build(in state, move, weapon);
                rig.Apply(in pose, holder.transform);
                boxes.Render(in state, move, weapon);

                Fit(model, in state, move, weapon, names[i]);

                Shot(outDir, "avatar-" + names[i], holder, 90f);
                Shot(outDir, "avatar-" + names[i] + "-front", holder, 0f);
            }

            Object.DestroyImmediate(holder);
        }

        /// <summary>
        /// The measurement this whole tool exists for: with the character posed, how much of it is
        /// outside the capsules the server shoots at?
        ///
        /// Looking at a screenshot is not enough and this project has been caught by that before - a
        /// limb half inside a translucent capsule reads as "fine" at a glance either way. So the
        /// posed skin is baked and every vertex is measured against the same hitbox the server builds,
        /// with the same test the blockout character is held to.
        /// </summary>
        static void Fit(GlbModel model, in PlayerSimState state, MovementTuning move,
                        WeaponTuning weapon, string stance)
        {
            PlayerHitbox box = PlayerHitbox.FromState(in state, move, weapon);

            int total = 0, outside = 0;
            float worst = 0f;
            string worstPart = "";
            HitZone worstZone = HitZone.None;
            List<string> parts = new List<string>();

            SkinnedMeshRenderer[] skins = model.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i].sharedMesh == null) continue;

                Mesh baked = new Mesh();
                skins[i].BakeMesh(baked);
                Vector3[] vertices = baked.vertices;
                // Position and rotation only. BakeMesh has already applied the renderer's scale, and
                // localToWorldMatrix would apply it a second time - which read as the whole character
                // being 82% outside its own hitbox, and was a bug in the ruler rather than the thing
                // being measured.
                Matrix4x4 toWorld = Matrix4x4.TRS(skins[i].transform.position,
                                                  skins[i].transform.rotation, Vector3.one);

                int partOutside = 0;
                float partWorst = 0f;
                for (int v = 0; v < vertices.Length; v++)
                {
                    Vec3 point = toWorld.MultiplyPoint3x4(vertices[v]).ToSim();
                    HitZone zone;
                    float d = Silhouette.Outside(point, in box, out zone);
                    total++;
                    if (d <= 0.002f) continue;
                    outside++;
                    partOutside++;
                    if (d > partWorst) partWorst = d;
                    if (d <= worst) continue;
                    worst = d;
                    worstPart = Label(skins[i]);
                    worstZone = zone;
                }
                Object.DestroyImmediate(baked);

                if (partOutside > 0)
                    parts.Add("      " + Label(skins[i]).PadRight(28)
                              + (100f * partOutside / Mathf.Max(1, vertices.Length)).ToString("0").PadLeft(3)
                              + "% out, worst " + (partWorst * 1000f).ToString("0") + " mm");
            }

            if (total == 0) return;
            float share = 100f * outside / total;
            Debug.Log("[fit] " + stance.PadRight(7) + share.ToString("0.0").PadLeft(5)
                      + "% of the character is outside its hitbox"
                      + (outside == 0 ? "" : "   worst " + (worst * 1000f).ToString("0") + " mm on "
                         + worstPart + " (nearest " + worstZone + ")"));
            for (int i = 0; i < parts.Count; i++) Debug.Log(parts[i]);
        }

        static string Label(SkinnedMeshRenderer skin)
        {
            return skin.sharedMesh != null && !string.IsNullOrEmpty(skin.sharedMesh.name)
                ? skin.sharedMesh.name : skin.name;
        }

        static void Shot(string outDir, string name, GameObject subject, float around)
        {
            const int width = 900;
            const int height = 900;

            GameObject camGo = new GameObject("cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;

            GameObject lightGo = new GameObject("key");
            lightGo.transform.rotation = Quaternion.Euler(38f, -40f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.4f, 0.43f, 0.5f);
            RenderSettings.fog = false;

            Bounds bounds = new Bounds(subject.transform.position + Vector3.up * 0.9f, Vector3.zero);
            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled) continue;
                if (!any) { bounds = renderers[i].bounds; any = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            float radius = Mathf.Max(0.4f, bounds.extents.magnitude);
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            Vector3 direction = Quaternion.Euler(-8f, around, 0f) * Vector3.forward;
            cam.transform.position = bounds.center + direction * distance;
            cam.transform.rotation = Quaternion.LookRotation((bounds.center - cam.transform.position).normalized,
                                                             Vector3.up);

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 8;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Debug.Log("[avatar] " + Path.Combine(outDir, name + ".png"));

            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
        }
    }
}
