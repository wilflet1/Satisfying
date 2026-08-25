using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Renders the duellist and the first-person sight picture to PNGs, so a body that has only ever
    /// been proved correct by arithmetic can actually be looked at.
    ///
    /// It drives the real view classes - RemotePlayerView and PlayerView - with real tuning, so what
    /// comes out is what the game draws, not a second opinion about it.
    /// </summary>
    public static class ShotSheet
    {
        const int Width = 1024;
        const int Height = 768;

        static string _outDir = "Screenshots";

        struct Shot
        {
            public string Name;
            public string Focus;        // which joint to frame on; "body" for all of him
            public float Radius;        // metres, ignored for "body"
            public float Around;        // degrees round him: 0 in front, 90 off his right
            public float Elevation;

            public Shot(string name, string focus, float radius, float around, float elevation = 0f)
            {
                Name = name; Focus = focus; Radius = radius; Around = around; Elevation = elevation;
            }
        }

        [MenuItem("Satisfying/Shots/Character sheet", priority = 60)]
        public static void Character()
        {
            Prepare();
            Palette palette = Palette.Build();
            MovementTuning move = new MovementTuning();
            WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();

            GameObject stage = Stage(palette, true);
            Camera cam = ShotCamera();

            Shot[] both = { new Shot("front", "body", 0f, 0f), new Shot("side", "body", 0f, 90f) };
            Shot[] joints =
            {
                new Shot("head", "head", 0.28f, 20f, 5f),
                new Shot("shoulder", "shoulders", 0.42f, 35f, 10f),
                new Shot("hips", "pelvis", 0.42f, 40f, 0f),
                new Shot("knee", "knee", 0.34f, 70f, 0f),
                new Shot("foot", "foot", 0.28f, 75f, 12f),
                new Shot("hands", "hands", 0.34f, 45f, 8f)
            };

            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Stand), 1, "stand", both);
            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Stand), 1, "stand", joints);
            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Crouch), 1, "crouch", both);
            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Crouch), 1, "crouch", joints);
            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Prone), 1, "prone", both);
            Sheet(cam, palette, move, weapons[0], Base(move, Stance.Prone), 1, "prone",
                new Shot[] { new Shot("top", "body", 0f, 90f, 62f), new Shot("head", "head", 0.4f, 35f, 12f) });

            PlayerNetState run = Base(move, Stance.Stand);
            run.Velocity = new Vec3(0f, 0f, 6.4f);
            Sheet(cam, palette, move, weapons[0], run, 26, "sprint", both);

            PlayerNetState slide = Base(move, Stance.Crouch);
            slide.Velocity = new Vec3(0f, 0f, 7.2f);
            slide.Sliding = true;
            Sheet(cam, palette, move, weapons[0], slide, 1, "slide", both);

            PlayerNetState dead = Base(move, Stance.Stand);
            dead.Alive = false;
            Sheet(cam, palette, move, weapons[0], dead, 45, "dead",
                new Shot[] { new Shot("side", "body", 0f, 90f), new Shot("angle", "body", 0f, 35f, 22f) });

            for (int w = 0; w < weapons.Length; w++)
            {
                PlayerNetState ads = Base(move, Stance.Stand);
                ads.Ads = 1f;
                ads.WeaponIndex = (byte)w;
                Sheet(cam, palette, move, weapons[w], ads, 1, "ads-w" + w,
                    new Shot[]
                    {
                        new Shot("side", "body", 0f, 90f),
                        new Shot("front", "body", 0f, 0f),
                        new Shot("hands", "hands", 0.45f, 50f, 12f)
                    });
            }

            PlayerNetState lean = Base(move, Stance.Stand);
            lean.Lean = 1f;
            Sheet(cam, palette, move, weapons[0], lean, 1, "lean", both);

            PlayerNetState vault = Base(move, Stance.Stand);
            vault.Vaulting = true;
            vault.Grounded = false;
            vault.Velocity = new Vec3(0f, 1.2f, 3.4f);
            Sheet(cam, palette, move, weapons[0], vault, 1, "vault", both);

            Object.DestroyImmediate(stage);
            Debug.Log("[shots] character sheet written to " + Path.GetFullPath(_outDir));
        }

        /// <summary>
        /// The sight picture, from inside the player's own head: every weapon, every sight, every
        /// stance. A magenta cross is drawn on the exact centre pixel of each frame afterwards, so
        /// "lined up on the centre of the screen" is something you can see rather than take on trust.
        /// </summary>
        [MenuItem("Satisfying/Shots/Sight pictures", priority = 61)]
        public static void Sights()
        {
            Prepare();
            Palette palette = Palette.Build();
            MovementTuning move = new MovementTuning();
            FeelTuning feel = new FeelTuning();
            WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();
            SightTuning[] sights = SightTuning.Defaults();

            GameObject stage = Stage(palette, true);
            Target(palette, new Vector3(0f, 0f, 22f));

            GameObject rig = new GameObject("first person");
            PlayerView view = new PlayerView(rig.transform, feel, palette, GameBootstrap.LayerViewmodel);

            Stance[] stances = { Stance.Stand, Stance.Crouch, Stance.Prone };
            string[] stanceNames = { "stand", "crouch", "prone" };

            for (int w = 0; w < weapons.Length; w++)
            {
                for (int g = 0; g < sights.Length; g++)
                {
                    for (int s = 0; s < stances.Length; s++)
                    {
                        PlayerSimState state = SimBase(move, stances[s]);
                        state.Weapon.Index = (byte)w;
                        state.Weapon.Sight = (byte)g;
                        state.Ads = 1f;

                        // Settle: the fov, the sway and the ADS blend are all springs, and a single
                        // frame would photograph them mid-flight.
                        Settle(view, in state, move, weapons[w], sights[g], 0f);
                        Capture(view, "ads-w" + w + "-s" + g + "-" + stanceNames[s], true);
                    }
                }
            }

            // And what you see looking down in each stance - the first-person body is drawn by the
            // same class as the opponent, so a leg in your face would be a leg in his too.
            RemotePlayerView own = new RemotePlayerView(rig.transform, 0, palette, move, GameBootstrap.LayerPlayer, true);
            for (int s = 0; s < stances.Length; s++)
            {
                PlayerSimState state = SimBase(move, stances[s]);
                PlayerNetState net = PlayerNetState.FromSim(0, in state, true, 100f);
                float impulse;
                own.Render(in net, 1f / 120f, weapons[0], out impulse);

                Settle(view, in state, move, weapons[0], sights[0], -78f);
                Capture(view, "down-" + stanceNames[s], false);

                PlayerSimState aiming = state;
                aiming.Ads = 1f;
                Settle(view, in aiming, move, weapons[0], sights[0], -60f);
                Capture(view, "down-ads-" + stanceNames[s], false);
            }

            Object.DestroyImmediate(rig);
            Object.DestroyImmediate(stage);
            Debug.Log("[shots] sight pictures written to " + Path.GetFullPath(_outDir));
        }

        /// <summary>
        /// The two things a hit produces that nobody has looked at: the blood the server's confirmation
        /// spawns, and what a head hit you survive does to your vision.
        /// </summary>
        [MenuItem("Satisfying/Shots/Hit effects", priority = 62)]
        public static void Hits()
        {
            Prepare();
            Palette palette = Palette.Build();
            MovementTuning move = new MovementTuning();
            FeelTuning feel = new FeelTuning();
            WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();
            SightTuning[] sights = SightTuning.Defaults();

            GameObject stage = Stage(palette, false);
            Camera cam = ShotCamera();

            // ---- blood, on a duellist, at the moment the round lands and a beat later.
            GameObject holder = new GameObject("subject");
            RemotePlayerView view = new RemotePlayerView(holder.transform, 1, palette, move,
                                                         GameBootstrap.LayerPlayer);
            PlayerNetState state = Base(move, Stance.Stand);
            float impulse;
            view.Render(in state, 1f / 64f, weapons[0], out impulse);

            CombatFx fx = new CombatFx(stage.transform, palette, GameBootstrap.LayerFx);
            PlayerSimState shown = state.ToDisplayState(move.staminaMax);
            BodyPose pose = BodyPose.Build(in shown, move, weapons[0]);

            Vector3 chest = pose.ChestBase.ToUnity() + Vector3.up * 0.2f;
            Vector3 along = new Vector3(0.15f, 0.05f, -1f).normalized;   // shot from the front, going through
            fx.Blood(chest, along, 0.7f);

            Bounds bounds = new Bounds(chest, Vector3.one * 1.5f);
            Frame(cam, bounds, 55f, 6f);
            Write(Render(cam, null), "blood-0-impact");

            for (int i = 0; i < 8; i++) fx.Update(1f / 60f);
            Write(Render(cam, null), "blood-1-spray");

            for (int i = 0; i < 14; i++) fx.Update(1f / 60f);
            Write(Render(cam, null), "blood-2-falling");

            // A head hit, which throws it from higher up and further.
            fx.Blood(pose.Head.ToUnity(), along, 1f);
            for (int i = 0; i < 6; i++) fx.Update(1f / 60f);
            Frame(cam, new Bounds(pose.Head.ToUnity(), Vector3.one * 1.4f), 50f, 8f);
            Write(Render(cam, null), "blood-3-head");

            Object.DestroyImmediate(holder);

            // ---- the concussion blur, at the strengths the three weapons actually produce.
            Target(palette, new Vector3(0f, 0f, 22f));
            GameObject rig = new GameObject("first person");
            PlayerView player = new PlayerView(rig.transform, feel, palette, GameBootstrap.LayerViewmodel);

            PlayerSimState aiming = SimBase(move, Stance.Stand);
            aiming.Ads = 1f;
            Settle(player, in aiming, move, weapons[0], sights[0], 0f);

            player.Blur.Clear();
            Capture(player, "blur-0-clear", false);

            for (int w = 0; w < weapons.Length; w++)
            {
                player.Blur.Clear();
                player.Blur.Hit(weapons[w].concussionStrength, weapons[w].concussionTime);
                Capture(player, "blur-w" + w + "-" + weapons[w].name.ToLowerInvariant(), false);
            }

            Object.DestroyImmediate(rig);
            Object.DestroyImmediate(stage);
            Debug.Log("[shots] hit effects written to " + Path.GetFullPath(_outDir));
        }

        static void Frame(Camera cam, Bounds bounds, float around, float elevation)
        {
            float radius = Mathf.Max(0.12f, bounds.extents.magnitude);
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            Vector3 direction = Quaternion.Euler(elevation, around, 0f) * new Vector3(0f, 0.10f, 1f).normalized;
            cam.transform.position = bounds.center + direction * distance;
            cam.transform.rotation = Quaternion.LookRotation((bounds.center - cam.transform.position).normalized,
                                                             Vector3.up);
        }

        /// <summary>
        /// The bolt gun and the picture through its glass, at both ends of the power ring. The scope
        /// is a second camera rendering into a texture, so this is the real thing and not a mock-up.
        /// </summary>
        [MenuItem("Satisfying/Shots/Scope", priority = 64)]
        public static void Scope()
        {
            Prepare();
            Palette palette = Palette.Build();
            MovementTuning move = new MovementTuning();
            FeelTuning feel = new FeelTuning();
            WeaponTuning[] weapons = WeaponTuning.DefaultLoadout();
            SightTuning[] sights = SightTuning.Defaults();

            GameObject stage = Stage(palette, true);
            Camera cam = ShotCamera();

            // The rifle in someone's hands, so the model can be judged as a silhouette.
            PlayerNetState ads = Base(move, Stance.Stand);
            ads.Ads = 1f;
            ads.WeaponIndex = 3;
            ads.SightIndex = (byte)SightKind.Scope;
            ads.Ammo = 5;
            Sheet(cam, palette, move, weapons[3], ads, 1, "sniper",
                new Shot[]
                {
                    new Shot("side", "body", 0f, 90f),
                    new Shot("front", "body", 0f, 0f),
                    new Shot("hands", "hands", 0.5f, 50f, 12f)
                });

            PlayerNetState hip = Base(move, Stance.Stand);
            hip.WeaponIndex = 3;
            hip.SightIndex = (byte)SightKind.Scope;
            hip.Ammo = 5;
            Sheet(cam, palette, move, weapons[3], hip, 1, "sniper-hip",
                new Shot[] { new Shot("side", "body", 0f, 90f) });

            // Something worth a scope: a man at sixty metres, dead ahead, plus posts further out.
            // A duellist is the honest test - if the centre dot does not land on him the optic is
            // pointing somewhere the rifle is not, and a coloured post would hide that.
            GameObject far = new GameObject("distant duellist");
            RemotePlayerView farView = new RemotePlayerView(far.transform, 2, palette, move,
                                                            GameBootstrap.LayerPlayer);
            PlayerNetState standing = Base(move, Stance.Stand);
            standing.Position = new Vec3(0f, 0f, 60f);
            standing.Yaw = 180f;
            float farStep;
            farView.Render(in standing, 1f / 64f, weapons[0], out farStep);

            Target(palette, new Vector3(3.5f, 0f, 120f));
            Target(palette, new Vector3(-6f, 0f, 220f));

            GameObject rig = new GameObject("first person");
            PlayerView player = new PlayerView(rig.transform, feel, palette, GameBootstrap.LayerViewmodel);

            PlayerSimState state = SimBase(move, Stance.Stand);
            state.Weapon.Index = 3;
            state.Weapon.Sight = (byte)SightKind.Scope;
            state.Weapon.Ammo = 5;
            state.Ads = 1f;

            SightTuning scope = sights[(int)SightKind.Scope];
            float[] powers = { 3.5f, 6f, 12f, 18f };
            for (int i = 0; i < powers.Length; i++)
            {
                player.Magnification = powers[i];
                Settle(player, in state, move, weapons[3], scope, 0f);
                CaptureScope(player, "scope-" + powers[i].ToString("0.0").Replace(".", "-") + "x");
            }

            // And what it looks like on the way up, which is when the eyebox is still finding you.
            state.Ads = 0.8f;
            player.Magnification = 6f;
            Settle(player, in state, move, weapons[3], scope, 0f);
            CaptureScope(player, "scope-coming-up");

            Object.DestroyImmediate(rig);
            Object.DestroyImmediate(far);
            Object.DestroyImmediate(stage);
            Debug.Log("[shots] scope written to " + Path.GetFullPath(_outDir));
        }

        /// <summary>
        /// The scope is drawn by the HUD in OnGUI, which a headless render does not run - so the shot
        /// sheet composites it the same way GameUI does: world, then viewmodel, then the glass.
        /// </summary>
        static void CaptureScope(PlayerView view, string name)
        {
            Texture2D frame = Render(view.Camera, view.WeaponCamera);

            ScopeView scope = view.Scope;
            if (scope != null && scope.Active && scope.Texture != null)
            {
                Write(ReadBack(scope.Texture), name + "-raw");
                Composite(frame, scope);
            }

            Write(frame, name);
        }

        static Texture2D ReadBack(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            Texture2D texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            return texture;
        }

        /// <summary>
        /// The scope, drawn onto a captured frame in software.
        ///
        /// The game draws it in OnGUI, which a headless render never runs - so this is a second
        /// blitter, and a second blitter is exactly the kind of thing that quietly starts drawing
        /// something the game does not. It is kept honest by owning none of the decisions: the circle,
        /// the reticle rect and all three textures come from ScopeView itself. All that lives here is
        /// "put these pixels over those pixels".
        /// </summary>
        static void Composite(Texture2D frame, ScopeView scope)
        {
            Texture2D picture = ReadBack(scope.Texture);
            Rect circle = scope.Circle(Width, Height);
            Rect reticle = scope.ReticleRect(Width, Height);

            Texture2D surround = scope.Surround;
            Texture2D shadow = scope.Shadow;
            Texture2D marks = scope.Reticle;

            for (int y = 0; y < Height; y++)
            {
                // GUI space runs down from the top; a texture runs up from the bottom.
                float guiY = Height - 1 - y;
                for (int x = 0; x < Width; x++)
                {
                    Color result = frame.GetPixel(x, y);

                    if (Inside(circle, x, guiY))
                    {
                        float u = (x - circle.x) / circle.width;
                        float v = 1f - (guiY - circle.y) / circle.height;
                        result = picture.GetPixelBilinear(u, v);
                        result = Over(result, shadow.GetPixelBilinear(u, 1f - v));
                    }

                    if (Inside(reticle, x, guiY))
                    {
                        float u = (x - reticle.x) / reticle.width;
                        float v = 1f - (guiY - reticle.y) / reticle.height;
                        Color mark = marks.GetPixelBilinear(u, v);
                        mark.r = 0.05f; mark.g = 0.05f; mark.b = 0.06f;
                        result = Over(result, mark);
                    }

                    result = Over(result, surround.GetPixelBilinear(x / (float)Width, 1f - guiY / (float)Height));
                    frame.SetPixel(x, y, result);
                }
            }
            frame.Apply();
            Object.DestroyImmediate(picture);
        }

        static bool Inside(Rect rect, float x, float y)
        {
            return x >= rect.x && x < rect.x + rect.width && y >= rect.y && y < rect.y + rect.height;
        }

        static Color Over(Color under, Color over)
        {
            float a = Mathf.Clamp01(over.a);
            return new Color(Mathf.Lerp(under.r, over.r, a),
                             Mathf.Lerp(under.g, over.g, a),
                             Mathf.Lerp(under.b, over.b, a), 1f);
        }

        public static void All()
        {
            Character();
            Sights();
            Hits();
            Scope();
        }

        static void Settle(PlayerView view, in PlayerSimState state, MovementTuning move, WeaponTuning weapon,
                           SightTuning sight, float pitch)
        {
            for (int i = 0; i < 300; i++)
                view.Render(in state, state.Position.ToUnity(), move, weapon, sight, 0f, pitch, 1f / 120f, false);
        }

        // ------------------------------------------------------------------ scene

        static void Prepare()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shotsOut") _outDir = args[i + 1];
            Directory.CreateDirectory(_outDir);

            if (!Application.isPlaying)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static GameObject Stage(Palette palette, bool facingMarker)
        {
            GameObject stage = new GameObject("stage");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "ground";
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = palette.Ground;
            ground.layer = GameBootstrap.LayerWorld;

            if (facingMarker)
            {
                // Which way he is facing, drawn on the floor. Every "is that foot on backwards?"
                // argument is settled by having the answer in the frame.
                GameObject arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arrow.name = "facing";
                arrow.transform.SetParent(stage.transform, false);
                arrow.transform.localPosition = new Vector3(0f, 0.004f, 0.55f);
                arrow.transform.localScale = new Vector3(0.03f, 0.008f, 1.1f);
                arrow.GetComponent<MeshRenderer>().sharedMaterial = palette.Accent;
                arrow.layer = GameBootstrap.LayerWorld;

                GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tip.name = "facing tip";
                tip.transform.SetParent(stage.transform, false);
                tip.transform.localPosition = new Vector3(0f, 0.004f, 1.05f);
                tip.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                tip.transform.localScale = new Vector3(0.12f, 0.008f, 0.12f);
                tip.GetComponent<MeshRenderer>().sharedMaterial = palette.Accent;
                tip.layer = GameBootstrap.LayerWorld;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.45f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.33f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.18f, 0.2f);

            Key(stage.transform, "key", new Vector3(46f, -35f, 0f), 1.15f, new Color(1f, 0.97f, 0.9f), true);
            Key(stage.transform, "fill", new Vector3(20f, 150f, 0f), 0.45f, new Color(0.72f, 0.8f, 1f), false);
            Key(stage.transform, "rim", new Vector3(8f, 205f, 0f), 0.35f, new Color(0.9f, 0.9f, 1f), false);
            return stage;
        }

        static void Key(Transform parent, string name, Vector3 euler, float intensity, Color color, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            // A contact shadow is the only thing in a still frame that says whether he is standing on
            // the floor or hovering half a metre above it.
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.75f;
        }

        /// <summary>Something to aim at, at a believable distance, so a sight picture has a subject.</summary>
        static void Target(Palette palette, Vector3 at)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "target";
            post.transform.position = at + new Vector3(0f, 1.55f, 0f);
            post.transform.localScale = new Vector3(0.55f, 0.85f, 0.12f);
            post.GetComponent<MeshRenderer>().sharedMaterial = palette.Accent;
            post.layer = GameBootstrap.LayerWorld;
        }

        static Camera ShotCamera()
        {
            GameObject go = new GameObject("shot camera");
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.cullingMask = ~0;
            return cam;
        }

        // ------------------------------------------------------------------ states

        static PlayerNetState Base(MovementTuning move, Stance stance)
        {
            PlayerNetState n = new PlayerNetState();
            n.Alive = true;
            n.Health = 100;
            n.Position = new Vec3(0f, 0f, 0f);
            n.Height = move.HeightFor(stance);
            n.Stance = stance;
            n.Grounded = true;
            n.Stamina = move.staminaMax;
            n.Ammo = 30;
            n.Yaw = 0f;
            return n;
        }

        static PlayerSimState SimBase(MovementTuning move, Stance stance)
        {
            PlayerSimState s = new PlayerSimState();
            s.Position = new Vec3(0f, 0f, 0f);
            s.Height = move.HeightFor(stance);
            s.Stance = stance;
            s.Grounded = true;
            s.Stamina = move.staminaMax;
            s.Weapon.Ammo = 30;
            return s;
        }

        // ------------------------------------------------------------------ shooting

        /// <summary>
        /// One duellist, one state, several framings. `ticks` runs the view forward first, which is how
        /// the walk cycle and the death fold get anywhere - both are time based and one frame catches
        /// them at zero.
        /// </summary>
        static void Sheet(Camera cam, Palette palette, MovementTuning move, WeaponTuning weapon,
                          PlayerNetState state, int ticks, string prefix, Shot[] shots)
        {
            GameObject holder = new GameObject("subject");
            RemotePlayerView view = new RemotePlayerView(holder.transform, 1, palette, move, GameBootstrap.LayerPlayer);

            float impulse;
            for (int i = 0; i < Mathf.Max(1, ticks); i++)
                view.Render(in state, 1f / 64f, weapon, out impulse);

            PlayerSimState shown = state.ToDisplayState(move.staminaMax);
            BodyPose pose = BodyPose.Build(in shown, move, weapon);

            for (int i = 0; i < shots.Length; i++)
            {
                Bounds bounds = shots[i].Focus == "body"
                    ? Framing(view.Character.Root)
                    : new Bounds(Joint(in pose, shots[i].Focus), Vector3.one * (shots[i].Radius * 2f));

                float radius = Mathf.Max(0.12f, bounds.extents.magnitude);
                float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
                Quaternion around = Quaternion.Euler(shots[i].Elevation, shots[i].Around, 0f);
                Vector3 direction = around * new Vector3(0f, 0.10f, 1f).normalized;

                cam.transform.position = bounds.center + direction * distance;
                cam.transform.rotation = Quaternion.LookRotation((bounds.center - cam.transform.position).normalized, Vector3.up);

                Write(Render(cam, null), prefix + "-" + shots[i].Name);
            }

            Object.DestroyImmediate(holder);
        }

        static Vector3 Joint(in BodyPose pose, string focus)
        {
            switch (focus)
            {
                case "head": return pose.Head.ToUnity();
                case "shoulders": return pose.RightShoulder.ToUnity();
                case "pelvis": return pose.Pelvis.ToUnity();
                case "knee": return pose.RightKnee.ToUnity();
                case "foot": return pose.RightAnkle.ToUnity();
                case "hands": return pose.RightHand.ToUnity();
                default: return pose.ChestBase.ToUnity();
            }
        }

        static Bounds Framing(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i].enabled || !renderers[i].gameObject.activeInHierarchy) continue;
                if (!any) { bounds = renderers[i].bounds; any = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        /// <summary>The world camera and the viewmodel camera, into one frame, the way the game does it.</summary>
        static void Capture(PlayerView view, string name, bool crosshair)
        {
            Texture2D shot = Render(view.Camera, view.WeaponCamera);
            if (crosshair)
            {
                // The zoom is taken before the cross is drawn on the full frame, so the crop is the
                // sight picture itself and not a picture of the marker.
                Write(Zoom(shot, 6), name + "-zoom");
                Cross(shot, 22, 5);
            }
            Write(shot, name);
        }

        /// <summary>
        /// The middle of the frame, blown up. A sight is a couple of hundred pixels across at ADS and
        /// "the post is in the notch" is not a judgement you can make from the whole screen.
        /// </summary>
        static Texture2D Zoom(Texture2D source, int factor)
        {
            int cropW = Width / factor;
            int cropH = Height / factor;
            int x0 = (Width - cropW) / 2;
            int y0 = (Height - cropH) / 2;

            Texture2D zoomed = new Texture2D(cropW * factor, cropH * factor, TextureFormat.RGB24, false);
            for (int y = 0; y < cropH * factor; y++)
                for (int x = 0; x < cropW * factor; x++)
                    zoomed.SetPixel(x, y, source.GetPixel(x0 + x / factor, y0 + y / factor));
            zoomed.Apply();
            Cross(zoomed, 60, 14);
            return zoomed;
        }

        static Texture2D Render(Camera first, Camera second)
        {
            RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 8;

            RenderTexture previous = first.targetTexture;
            first.targetTexture = rt;
            first.Render();
            first.targetTexture = previous;

            if (second != null)
            {
                RenderTexture previousSecond = second.targetTexture;
                second.targetTexture = rt;
                second.Render();
                second.targetTexture = previousSecond;
            }

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;

            rt.Release();
            Object.DestroyImmediate(rt);
            return texture;
        }

        /// <summary>Marks the exact centre pixel. Half a millimetre of sight misalignment is a test's
        /// job; a sight sitting visibly off centre is this cross's.</summary>
        static void Cross(Texture2D texture, int arm, int gap)
        {
            int cx = texture.width / 2;
            int cy = texture.height / 2;
            Color mark = new Color(1f, 0f, 1f);
            for (int i = -arm; i <= arm; i++)
            {
                if (i > -gap && i < gap) continue;   // leave the middle clear so the sight shows through
                texture.SetPixel(cx + i, cy, mark);
                texture.SetPixel(cx, cy + i, mark);
            }
            texture.Apply();
        }

        static void Write(Texture2D texture, string name)
        {
            string path = Path.Combine(_outDir, name + ".png");
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            Debug.Log("[shots] " + path);
        }
    }
}
