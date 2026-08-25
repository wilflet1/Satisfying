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
        public void Render(Transform opticAxis, float fieldOfView, float magnification, float blend)
        {
            Blend = Mathf.Clamp01(blend);
            Magnification = magnification;

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

            // 2. the eye relief shadow, which is what makes it read as glass rather than a hole
            GUI.DrawTexture(circle, _shadow, ScaleMode.StretchToFill, true);

            // 3. the reticle
            GUI.color = new Color(0.05f, 0.05f, 0.06f, Blend);
            GUI.DrawTexture(ReticleRect(screenWidth, screenHeight), _reticle, ScaleMode.StretchToFill, true);

            // 4. the body of the scope, over everything, hiding the square corners of the picture
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
        public Texture2D Shadow { get { EnsureTextures(); return _shadow; } }
        public Texture2D Reticle { get { EnsureTextures(); return _reticle; } }

        void EnsureTextures()
        {
            EnsureShadow();
            EnsureReticle();
            EnsureSurround();
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
            float thin = size * 0.0022f;
            float thick = size * 0.0075f;
            float mil = size * 0.052f;          // one mil, in texels

            // The four stadia. Thin near the middle so they do not swallow the target, thickening
            // towards the edge so the eye finds the centre fast in a busy picture.
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px - centre;
                    float dy = py - centre;
                    float ax = Mathf.Abs(dx);
                    float ay = Mathf.Abs(dy);

                    float alpha = 0f;

                    // Horizontal and vertical stadia, from 1 mil out to the edge of the glass.
                    float taper = size * 0.0022f;
                    if (ay <= Width(ax, size, thin, thick, taper) && ax > mil * 0.9f && ax < size * 0.46f) alpha = 1f;
                    if (ax <= Width(ay, size, thin, thick, taper) && ay > mil * 0.9f && ay < size * 0.46f) alpha = 1f;

                    // The floating centre dot: small, and the only solid thing in the middle.
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= size * 0.0055f) alpha = 1f;

                    // The horseshoe - a ring broken at the top, so it frames a target at low power
                    // without hiding what is above it.
                    float ringRadius = mil * 2.6f;
                    float ringWidth = size * 0.0042f;
                    if (Mathf.Abs(d - ringRadius) <= ringWidth)
                    {
                        // Open across the top third.
                        bool openTop = dy > 0f && ay > ax * 1.1f;
                        if (!openTop) alpha = 1f;
                    }

                    if (alpha > 0f) pixels[py * size + px] = new Color(0f, 0f, 0f, alpha);
                }
            }

            // Mil hashes: below the centre for holdover, and either side for wind. Below goes further,
            // because that is the half anyone uses.
            for (int m = 1; m <= 10; m++)
            {
                float offset = mil * m;
                if (offset > size * 0.44f) break;

                // Longer every fifth mark, and a number's worth of length on the tens.
                float length = (m % 5 == 0) ? size * 0.026f : size * 0.013f;
                Hash(pixels, size, centre, centre - offset, length, true);      // holdover, under the cross
                if (m <= 6)
                {
                    Hash(pixels, size, centre - offset, centre, length * 0.8f, false);
                    Hash(pixels, size, centre + offset, centre, length * 0.8f, false);
                }
            }

            // The tree: a couple of rows of wind dots hung off the holdover stadia, which is what
            // makes it a precision reticle rather than a crosshair with ticks.
            for (int row = 2; row <= 8; row += 2)
            {
                float y = centre - mil * row;
                int wings = row / 2;
                for (int w = 1; w <= wings; w++)
                {
                    Dot(pixels, size, centre - mil * w * 0.9f, y, size * 0.0038f);
                    Dot(pixels, size, centre + mil * w * 0.9f, y, size * 0.0038f);
                }
            }

            _reticle.SetPixels(pixels);
            _reticle.Apply();
        }

        /// <summary>Stadia thickness: hairline in the middle of the glass, heavier out at the edge.</summary>
        static float Width(float distance, int size, float thin, float thick, float taper)
        {
            float k = Mathf.Clamp01((distance - size * 0.16f) / (size * 0.26f));
            return Mathf.Lerp(thin, thick, k * k);
        }

        static void Hash(Color[] pixels, int size, float x, float y, float length, bool vertical)
        {
            float halfThickness = size * 0.0026f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(x - (vertical ? length : halfThickness)));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(x + (vertical ? length : halfThickness)));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(y - (vertical ? halfThickness : length)));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(y + (vertical ? halfThickness : length)));

            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                    pixels[py * size + px] = new Color(0f, 0f, 0f, 1f);
        }

        static void Dot(Color[] pixels, int size, float x, float y, float radius)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(x - radius));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(x + radius));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(y - radius));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(y + radius));

            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px - x;
                    float dy = py - y;
                    if (dx * dx + dy * dy <= radius * radius)
                        pixels[py * size + px] = new Color(0f, 0f, 0f, 1f);
                }
            }
        }
    }
}
