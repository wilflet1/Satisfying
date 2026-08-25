using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The picture through a magnified optic.
    ///
    /// It is a real picture in picture: a second camera sits on the optical axis of the scope model,
    /// looking down it at a narrow field of view, and renders into a texture that is drawn as a circle
    /// in the middle of the screen. The main camera is left alone at its normal field of view, which
    /// is the whole reason to do it this way - the rifle, your hands and the world around the tube
    /// stay in proper perspective instead of the entire screen zooming, and the black ring is the
    /// scope body rather than a picture of one.
    ///
    /// It is also cheaper than it sounds, and deliberately so:
    ///
    ///   - the texture is square and small (768 by default), not a second full-screen buffer. The
    ///     circle it lands in is about a third of a 1080p screen's height, so anything bigger is
    ///     resolution nobody sees.
    ///   - it only exists while a scope is actually up. Coming off the sights releases it.
    ///   - the viewmodel layer is culled from it, so the gun is not drawn a second time - and the
    ///     rifle is not in front of its own objective lens anyway.
    ///   - nothing is allocated per frame: one camera, one texture, three generated textures.
    ///
    /// The surround and the reticle are generated Texture2Ds rather than a shader, in keeping with
    /// the rest of the project - there is not a single asset file in it and there is no reason for
    /// this to be the first one.
    /// </summary>
    public sealed class ScopeView
    {
        /// <summary>Side of the scope render target. The circle drawn on screen is smaller than this.</summary>
        const int Resolution = 768;

        readonly Camera _camera;
        readonly Transform _rig;
        RenderTexture _target;

        Texture2D _surround;        // black, with a soft-edged hole in the middle
        Texture2D _reticle;
        Texture2D _shadow;          // the crescent of eye-relief shadow

        /// <summary>Magnification the player has dialled in, remembered across raising and lowering.</summary>
        public float Magnification = 6f;

        /// <summary>0 to 1: how far up the scope is. The picture fades and grows in over the last of it.</summary>
        public float Blend;

        /// <summary>
        /// Where the eye is sitting in the eyebox, in fractions of the tube's radius. Turning fast
        /// pushes it off centre, which is what a real scope does to you and is the single cheapest
        /// thing that makes one feel like glass rather than a hole cut in the HUD.
        /// </summary>
        Vector2 _eyeOffset;
        Vector2 _eyeVelocity;

        public bool Active { get { return Blend > 0.01f; } }
        public RenderTexture Texture { get { return _target; } }

        /// <summary>Where the optic is and where it is pointed. Read by the shot sheet, which has no
        /// other way to tell a camera aimed at the wrong thing from a camera drawn wrongly.</summary>
        public Vector3 Eye { get { return _camera.transform.position; } }
        public Vector3 Aim { get { return _camera.transform.forward; } }
        public float FieldOfView { get { return _camera.fieldOfView; } }

        public ScopeView(Transform parent, int worldMask)
        {
            GameObject rig = new GameObject("Scope Rig");
            rig.transform.SetParent(parent, false);
            _rig = rig.transform;

            GameObject go = new GameObject("Scope Camera");
            go.transform.SetParent(_rig, false);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 900f;         // a scope is for looking a long way
            _camera.cullingMask = worldMask;
            _camera.depth = -10f;                // renders before the main camera, into its own texture
            _camera.enabled = false;             // driven by hand, so it costs nothing when stowed
        }

        /// <summary>
        /// Points the scope camera down the optic and renders it. Called once a frame while aiming a
        /// magnified sight, and not at all otherwise.
        /// </summary>
        public void Render(Transform opticAxis, float fieldOfView, float magnification, float blend,
                           float turnRate, float pitchRate, float dt)
        {
            Blend = Mathf.Clamp01(blend);
            Magnification = magnification;

            // Eye relief. The faster you swing the rifle the further your eye falls out of the eyebox,
            // and the shadow crescent comes in from the side you swung away from. It springs back when
            // you settle, so a scope you are holding still is a clean circle.
            //
            // It scales with magnification because that is how eyeboxes work: 18x is unforgiving and
            // 3.5x you can be halfway off and still see everything.
            float forgiveness = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(3.5f, 18f, magnification));
            Vector2 target = new Vector2(Mathf.Clamp(-turnRate * 0.010f, -1.2f, 1.2f),
                                         Mathf.Clamp(pitchRate * 0.010f, -1.2f, 1.2f)) * forgiveness;
            float step = 1f - Mathf.Exp(-14f * Mathf.Max(0.0001f, dt));
            _eyeVelocity = Vector2.Lerp(_eyeVelocity, target, step);
            _eyeOffset = Vector2.Lerp(_eyeOffset, _eyeVelocity, step);

            if (!Active || opticAxis == null)
            {
                Release();
                return;
            }

            if (_target == null)
            {
                // sRGB, explicitly. The scope picture is drawn to the screen with Graphics.DrawTexture,
                // which samples it as an ordinary texture - and a linear target sampled that way comes
                // out washed white, which is exactly what the first version of this did.
                _target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32,
                                            RenderTextureReadWrite.sRGB);
                _target.name = "scope";
                _target.antiAliasing = 4;
                _target.filterMode = FilterMode.Bilinear;
                _target.Create();
            }

            // Straight down the tube, from the tube. Not from the player's eye: the whole point of a
            // scope picture is that it is the OPTIC's view, and at 18x the few centimetres between
            // the two is a visible parallax shift on anything close.
            _camera.transform.position = opticAxis.position;
            _camera.transform.rotation = opticAxis.rotation;
            _camera.fieldOfView = Mathf.Clamp(fieldOfView / Mathf.Max(1f, magnification), 0.6f, 90f);

            _camera.targetTexture = _target;
            _camera.Render();
            _camera.targetTexture = null;
        }

        public void Release()
        {
            if (_target == null) return;
            _target.Release();
            Object.Destroy(_target);
            _target = null;
        }

        // ================================================================== drawing

        /// <summary>
        /// Draws the scope over the middle of the screen. Called from the HUD's OnGUI, after the world
        /// and before anything else, so the reticle sits under the hit marker and the kill feed.
        /// </summary>
        public void Draw(float screenWidth, float screenHeight, float scale)
        {
            if (!Active || _target == null) return;

            EnsureTextures();
            Rect circle = Circle(screenWidth, screenHeight);
            Color previous = GUI.color;

            // GUI.DrawTexture rather than Graphics.DrawTexture. The low level one is tempting because
            // it can be called outside OnGUI, but it blits without the colour space conversion and the
            // picture comes out washed to white - which is exactly what it did.

            // 1. the picture
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(circle, _target, ScaleMode.ScaleAndCrop, false);

            // 2. the eye relief shadow, offset by where the eye is sitting. Drawn oversized and
            //    slid about, so the crescent closes in from one side rather than fading evenly.
            float travel = circle.width * 0.16f;
            Rect shadowRect = new Rect(circle.x + _eyeOffset.x * travel, circle.y - _eyeOffset.y * travel,
                                       circle.width, circle.height);
            GUI.DrawTexture(shadowRect, _shadow, ScaleMode.StretchToFill, true);

            // 3. the glass itself: a soft sheen across the top left, and a cool cast over the lot.
            //    Two textures and no shader, and it is the difference between a picture in a circle
            //    and something you are looking through.
            GUI.color = new Color(1f, 1f, 1f, Blend * 0.5f);
            GUI.DrawTexture(circle, _sheen, ScaleMode.StretchToFill, true);

            // 4. the reticle
            GUI.color = new Color(0.05f, 0.05f, 0.06f, Blend);
            GUI.DrawTexture(ReticleRect(screenWidth, screenHeight), _reticle, ScaleMode.StretchToFill, true);

            // 5. the body of the scope, over everything, hiding the square corners of the picture
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(new Rect(0f, 0f, screenWidth, screenHeight), _surround, ScaleMode.StretchToFill, true);

            GUI.color = previous;
        }

        /// <summary>
        /// Where the glass sits. Public because the shot sheet composites the same layout in software -
        /// OnGUI does not run in a headless render - and two different ideas about where the circle is
        /// would make the screenshots a picture of something the game does not draw.
        /// </summary>
        public Rect Circle(float screenWidth, float screenHeight)
        {
            // A scope picture that filled the screen would be the same thing as just zooming the
            // camera. Keeping it to two thirds of the height is what leaves room for the rifle and
            // the world around it, which is the effect worth having. It grows in over the last of the
            // aim rather than appearing at full size, which reads as the eye finding the eyebox.
            float diameter = Mathf.Min(screenWidth, screenHeight) * 0.66f * Mathf.Lerp(0.86f, 1f, Blend);
            return new Rect((screenWidth - diameter) * 0.5f, (screenHeight - diameter) * 0.5f, diameter, diameter);
        }

        /// <summary>
        /// First focal plane: the marks grow with the picture, so a hold is the same hold at any power,
        /// which is the entire reason anyone buys one.
        /// </summary>
        public Rect ReticleRect(float screenWidth, float screenHeight)
        {
            float focalPlane = Mathf.Clamp(Magnification / 6f, 0.55f, 2.6f);
            float size = Circle(screenWidth, screenHeight).width * 0.92f * focalPlane;
            return new Rect((screenWidth - size) * 0.5f, (screenHeight - size) * 0.5f, size, size);
        }

        public Texture2D Surround { get { EnsureTextures(); return _surround; } }
        public Texture2D Sheen { get { EnsureTextures(); return _sheen; } }

        /// <summary>Where the eye is in the eyebox, so the shot sheet can put the shadow where the
        /// game puts it rather than guessing.</summary>
        public Vector2 EyeOffset { get { return _eyeOffset; } }
        public Texture2D Shadow { get { EnsureTextures(); return _shadow; } }
        public Texture2D Reticle { get { EnsureTextures(); return _reticle; } }

        void EnsureTextures()
        {
            EnsureShadow();
            EnsureReticle();
            EnsureSurround();
            EnsureSheen();
        }

        /// <summary>
        /// The reflection on the glass: a broad soft streak across the top left, and the faintest cool
        /// wash over everything. Real glass is never perfectly clear and the eye reads "clear" as
        /// "hole", so this is what stops the picture looking like a cut-out.
        /// </summary>
        Texture2D _sheen;

        void EnsureSheen()
        {
            if (_sheen != null) return;

            const int size = 128;
            _sheen = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _sheen.wrapMode = TextureWrapMode.Clamp;
            _sheen.filterMode = FilterMode.Bilinear;

            for (int py = 0; py < size; py++)
            {
                float ny = py / (float)(size - 1) * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    float nx = px / (float)(size - 1) * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    if (d > 1f) { _sheen.SetPixel(px, py, new Color(0f, 0f, 0f, 0f)); continue; }

                    // A band running down and right, brightest towards the top left of the lens.
                    float band = nx * 0.7f - ny * 0.7f;
                    float streak = Mathf.Exp(-(band + 0.55f) * (band + 0.55f) * 9f) * 0.55f;
                    streak += Mathf.Exp(-(band + 0.05f) * (band + 0.05f) * 40f) * 0.22f;

                    // Nothing right at the rim, where the tube shades it anyway.
                    streak *= Mathf.Clamp01((0.98f - d) / 0.25f);

                    float wash = 0.06f * Mathf.Clamp01(1f - d);
                    float alpha = Mathf.Clamp01(streak + wash);
                    _sheen.SetPixel(px, py, new Color(0.78f, 0.88f, 0.95f, alpha));
                }
            }
            _sheen.Apply();
        }

        /// <summary>
        /// The scope body: opaque everywhere except a circle the size of the picture. Regenerated when
        /// the window changes shape, which is the only thing that moves the hole.
        /// </summary>
        int _surroundWidth, _surroundHeight;

        void EnsureSurround()
        {
            int width = 256;
            int height = 256;
            if (_surround != null && _surroundWidth == width && _surroundHeight == height) return;

            _surroundWidth = width;
            _surroundHeight = height;
            if (_surround != null) Object.Destroy(_surround);

            _surround = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _surround.wrapMode = TextureWrapMode.Clamp;
            _surround.filterMode = FilterMode.Bilinear;

            // The hole has to line up with the circle the picture is drawn in, and that circle is
            // 0.66 of the SMALLER screen dimension - so in a texture stretched over the whole screen
            // the hole is an ellipse. Working in normalised screen space and letting the stretch do
            // the rest keeps this correct at any aspect ratio without regenerating anything.
            float aspect = Mathf.Max(0.2f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float radiusY = 0.33f;
            float radiusX = aspect >= 1f ? radiusY / aspect : radiusY;
            if (aspect < 1f) radiusY = radiusX * aspect;

            for (int py = 0; py < height; py++)
            {
                float ny = py / (float)(height - 1) - 0.5f;
                for (int px = 0; px < width; px++)
                {
                    float nx = px / (float)(width - 1) - 0.5f;
                    float d = Mathf.Sqrt((nx / radiusX) * (nx / radiusX) + (ny / radiusY) * (ny / radiusY));
                    // Hard inside, hard outside, one texel of softness on the rim so it is not jagged.
                    float alpha = Mathf.Clamp01((d - 0.985f) / 0.03f);
                    _surround.SetPixel(px, py, new Color(0.02f, 0.02f, 0.025f, alpha));
                }
            }
            _surround.Apply();
        }

        /// <summary>A dark crescent round the inside of the tube. Cheap, and it does most of the work
        /// of making a flat texture look like it is at the bottom of a metre of glass.</summary>
        void EnsureShadow()
        {
            if (_shadow != null) return;

            const int size = 128;
            _shadow = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _shadow.wrapMode = TextureWrapMode.Clamp;
            _shadow.filterMode = FilterMode.Bilinear;

            for (int py = 0; py < size; py++)
            {
                float ny = py / (float)(size - 1) * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    float nx = px / (float)(size - 1) * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);

                    // Nothing until well out towards the rim, then it comes on fast. Too much of this
                    // and the usable picture is a coin in the middle of a black disc.
                    float vignette = Mathf.Clamp01((d - 0.82f) / 0.18f);
                    vignette *= vignette;

                    // Offset a little up and left, so the shadow is not perfectly symmetrical - a
                    // scope you are not perfectly behind is what one actually looks like.
                    float bias = Mathf.Clamp01(0.5f + (nx * 0.35f + ny * 0.25f));
                    float alpha = Mathf.Clamp01(vignette * Mathf.Lerp(1f, 0.72f, bias));

                    _shadow.SetPixel(px, py, new Color(0f, 0f, 0f, alpha * 0.85f));
                }
            }
            _shadow.Apply();
        }

        /// <summary>
        /// The reticle: a fine cross with a floating centre dot, a horseshoe opening downwards, and
        /// milled stadia with holdover marks under the centre. Drawn into a texture once rather than
        /// as a hundred GUI rects a frame.
        ///
        /// Everything is measured in fractions of the texture so it scales cleanly, and the lines are
        /// deliberately thin - a reticle you can hide a man behind is not one you can shoot with.
        /// </summary>
        void EnsureReticle()
        {
            if (_reticle != null) return;

            const int size = 512;
            _reticle = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _reticle.wrapMode = TextureWrapMode.Clamp;
            _reticle.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0f, 0f, 0f, 0f);

            float centre = (size - 1) * 0.5f;
            float mil = size * 0.055f;

            // Four plain stadia in from the edge, stopping well short of the middle. The previous one
            // had a horseshoe, a floating dot, wind dots and a holdover tree all fighting each other
            // in the same two centimetres of glass; you could not see a man behind it. What is left is
            // what a precision reticle is actually made of: a cross you can find, a gap you can see
            // through, and marks you can count.
            float inner = mil * 1.15f;
            float outer = size * 0.455f;
            float thin = size * 0.0026f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px - centre;
                    float dy = py - centre;
                    float ax = Mathf.Abs(dx);
                    float ay = Mathf.Abs(dy);
                    float alpha = 0f;

                    // The stadia thicken towards the rim so the eye is led inwards.
                    if (ax > inner && ax < outer && ay <= Taper(ax, size, thin)) alpha = 1f;
                    if (ay > inner && ay < outer && ax <= Taper(ay, size, thin)) alpha = 1f;

                    // One small dot in the middle, and nothing else anywhere near it.
                    if (Mathf.Sqrt(dx * dx + dy * dy) <= size * 0.0042f) alpha = 1f;

                    if (alpha > 0f) pixels[py * size + px] = new Color(0f, 0f, 0f, alpha);
                }
            }

            // Mil marks: below the centre for holdover and either side for wind, every mil, with a
            // longer one every fifth. Ticks only - no dots, no tree.
            for (int m = 1; m <= 8; m++)
            {
                float offset = mil * m;
                if (offset > outer - size * 0.01f) break;
                float length = (m % 5 == 0) ? size * 0.022f : size * 0.011f;

                Tick(pixels, size, centre, centre - offset, length, true);
                if (m <= 5)
                {
                    Tick(pixels, size, centre - offset, centre, length * 0.85f, false);
                    Tick(pixels, size, centre + offset, centre, length * 0.85f, false);
                }
            }

            _reticle.SetPixels(pixels);
            _reticle.Apply();
        }

        /// <summary>Hairline in the middle of the glass, a little heavier out at the rim.</summary>
        static float Taper(float distance, int size, float thin)
        {
            float k = Mathf.Clamp01((distance - size * 0.20f) / (size * 0.24f));
            return Mathf.Lerp(thin, thin * 2.6f, k * k);
        }

        static void Tick(Color[] pixels, int size, float x, float y, float length, bool horizontal)
        {
            float half = size * 0.0022f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(x - (horizontal ? length : half)));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(x + (horizontal ? length : half)));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(y - (horizontal ? half : length)));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(y + (horizontal ? half : length)));

            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                    pixels[py * size + px] = new Color(0f, 0f, 0f, 1f);
        }

    }
}
