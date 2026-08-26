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
        // 1024, and point-filtered on the way out. The picture is drawn into a circle about two
        // thirds of the screen height, so on a 1440p monitor a 768 texture is being magnified and the
        // result is a soft, smeary lens - which is exactly the "blurry round the edge" complaint.
        const int Resolution = 1024;

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
            float forgiveness = Mathf.Lerp(0.2f, 1f, Mathf.InverseLerp(1f, 18f, magnification));
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
            GUI.color = new Color(1f, 1f, 1f, Blend * 0.35f);
            GUI.DrawTexture(circle, _sheen, ScaleMode.StretchToFill, true);

            // 4. the reticle: the etched glass first, then the illuminated centre and its bloom on
            //    top of it. The lit part is drawn twice - once wide and dim for the glow, once tight
            //    and bright - which is what an illuminated reticle looks like through glass.
            Rect reticle = ReticleRect(screenWidth, screenHeight);

            // Glow underneath, then the reticle itself over it. Two passes and it reads as lit glass.
            GUI.color = new Color(1f, 1f, 1f, Blend * 0.5f);
            GUI.DrawTexture(reticle, _glow, ScaleMode.StretchToFill, true);
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(reticle, _reticle, ScaleMode.StretchToFill, true);

            // 5. the housing, as a ring round the lens. Everything outside it is the world, drawn by
            //    the main camera, at the field of view it always had.
            GUI.color = new Color(1f, 1f, 1f, Blend);
            GUI.DrawTexture(Rim(circle), _surround, ScaleMode.StretchToFill, true);

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
        /// <summary>How far past the lens the housing reaches, as a multiple of the lens radius.</summary>
        public const float RimSpan = 1.85f;

        /// <summary>The square the housing ring is drawn into, centred on the lens.</summary>
        public static Rect Rim(Rect circle)
        {
            float grow = circle.width * (RimSpan - 1f) * 0.5f;
            return new Rect(circle.x - grow, circle.y - grow, circle.width + grow * 2f, circle.height + grow * 2f);
        }

        public Rect ReticleRect(float screenWidth, float screenHeight)
        {
            // The reticle GROWS with the magnification, the way a first focal plane optic does - a
            // hold is then the same hold at any power, which is the whole reason to have one - but it
            // is CLAMPED to the glass. Past the point where it would fill the lens it simply stops
            // growing, so it never runs out over the housing and off the screen the way it used to.
            float lens = Circle(screenWidth, screenHeight).width;
            float scale = Magnification / ReferencePower;
            float size = lens * 0.80f * scale;
            size = Mathf.Clamp(size, lens * 0.34f, lens * 0.98f);
            return new Rect((screenWidth - size) * 0.5f, (screenHeight - size) * 0.5f, size, size);
        }

        /// <summary>The power the reticle is drawn at its nominal size. Middle of the range.</summary>
        const float ReferencePower = 8f;

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

                    // One narrow streak across the top left corner of the glass and nothing else.
                    // The old one had a wide band and a wash over the whole lens, which is a fog:
                    // it took the contrast out of the picture and read as the scope being dirty.
                    float band = nx * 0.7f - ny * 0.7f;
                    float streak = Mathf.Exp(-(band + 0.62f) * (band + 0.62f) * 26f) * 0.34f;

                    // Nothing near the rim, where the tube shades it anyway.
                    streak *= Mathf.Clamp01((0.94f - d) / 0.30f);
                    float alpha = Mathf.Clamp01(streak);
                    _sheen.SetPixel(px, py, new Color(0.78f, 0.88f, 0.95f, alpha));
                }
            }
            _sheen.Apply();
        }

        /// <summary>
        /// The scope body: a ring of housing round the lens, and NOTHING beyond it.
        ///
        /// The first version of this was opaque everywhere outside the lens, which blacked out the
        /// entire screen and made the picture-in-picture pointless - the whole reason for a second
        /// camera is that the world around the tube carries on being the world. It is a rim now: hard
        /// black for the thickness of the ocular housing, then gone.
        /// </summary>
        int _surroundSize;

        void EnsureSurround()
        {
            const int size = 512;
            if (_surround != null && _surroundSize == size) return;
            _surroundSize = size;
            if (_surround != null) Object.Destroy(_surround);

            _surround = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _surround.wrapMode = TextureWrapMode.Clamp;
            _surround.filterMode = FilterMode.Bilinear;

            // Drawn into a square the size of the lens times RimSpan, so the ring has room to live
            // outside the glass without the texture having to know the shape of the window.
            for (int py = 0; py < size; py++)
            {
                float ny = py / (float)(size - 1) * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    float nx = px / (float)(size - 1) * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny) * RimSpan;

                    float alpha;
                    Color colour;
                    if (d < 0.995f)
                    {
                        // Inside the glass: nothing at all.
                        alpha = 0f;
                        colour = new Color(0f, 0f, 0f, 0f);
                    }
                    else if (d < 1.055f)
                    {
                        // The very edge of the lens, where the coating catches. A thin bright line is
                        // what makes it read as glass sitting in metal rather than a hole in a wall.
                        float k = Mathf.InverseLerp(0.995f, 1.055f, d);
                        alpha = 1f;
                        colour = Color.Lerp(new Color(0.55f, 0.72f, 0.85f), new Color(0.10f, 0.11f, 0.13f), k);
                    }
                    else if (d < 1.46f)
                    {
                        // The housing. It has to reach past root two - the corner of the square render
                        // texture - or the corners of the picture show outside the ring.
                        float k = Mathf.InverseLerp(1.055f, 1.46f, d);
                        alpha = 1f;
                        colour = Color.Lerp(new Color(0.10f, 0.11f, 0.13f), new Color(0.045f, 0.05f, 0.06f), k);
                    }
                    else
                    {
                        // Beyond the housing the world shows through, with a short soft edge so the
                        // ring does not have a hard sawn-off outline.
                        alpha = Mathf.Clamp01(1f - (d - 1.46f) / 0.22f);
                        colour = new Color(0.045f, 0.05f, 0.06f);
                    }

                    _surround.SetPixel(px, py, new Color(colour.r, colour.g, colour.b, alpha));
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

                    // Nothing at all until the very edge of the glass, and then it comes on hard.
                    // A soft gradient over the outer fifth of the lens is what made the picture look
                    // out of focus - the eye reads a slow darkening as blur.
                    float vignette = Mathf.Clamp01((d - 0.93f) / 0.07f);

                    // Offset a little up and left, so the shadow is not perfectly symmetrical - a
                    // scope you are not perfectly behind is what one actually looks like.
                    float bias = Mathf.Clamp01(0.5f + (nx * 0.35f + ny * 0.25f));
                    float alpha = Mathf.Clamp01(vignette * Mathf.Lerp(1f, 0.72f, bias));

                    _shadow.SetPixel(px, py, new Color(0f, 0f, 0f, alpha * 0.9f));
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
        /// <summary>
        /// A clean illuminated reticle: a thin dark cross for the etched glass, and a red holographic
        /// centre sitting on top of it - a small chevron under a floating dot, with a soft bloom round
        /// both. Two textures rather than one, because the etched part and the lit part are drawn in
        /// different colours and the lit part has to glow.
        ///
        /// Everything is measured in fractions of the texture so it scales cleanly, and the etched
        /// lines are hairline on purpose. A reticle you can hide a man behind is not one you can
        /// shoot with, which is what the last one did.
        /// </summary>
        /// <summary>
        /// A red circle with a dot in the middle of it, and nothing else.
        ///
        /// It used to be an etched black cross with the red centre sitting on top. The cross was the
        /// problem: black lines over a dark target are invisible exactly when you need them, and four
        /// stadia across the glass is a lot of furniture to look past. A ring is the oldest reticle
        /// there is because it works - the eye centres a circle on a target without being told to,
        /// and the middle stays completely clear.
        ///
        /// All of it is lit, so all of it goes through the bloom pass and reads as glass rather than
        /// as paint.
        /// </summary>
        void EnsureReticle()
        {
            if (_reticle != null) return;

            const int size = 512;
            _reticle = Blank(size);
            _glow = Blank(size);

            Color[] lit = new Color[size * size];
            for (int i = 0; i < lit.Length; i++) lit[i] = new Color(0f, 0f, 0f, 0f);

            float centre = (size - 1) * 0.5f;
            float ring = size * 0.300f;         // radius of the circle
            float thickness = size * 0.0125f;   // how heavy the line is
            float dot = size * 0.0090f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    float dx = px - centre;
                    float dy = py - centre;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = 0f;

                    // The ring, with a soft edge so it does not crawl when the reticle is scaled.
                    float onRing = 1f - Mathf.Clamp01((Mathf.Abs(d - ring) - thickness) / (thickness * 0.9f));
                    if (onRing > 0f) alpha = onRing;

                    // The dot, dead on the aim point.
                    float onDot = 1f - Mathf.Clamp01((d - dot) / (dot * 0.8f));
                    if (onDot > alpha) alpha = onDot;

                    // Four short spurs just outside the ring, at the cardinals. They give the eye a
                    // level to judge cant against, and they are the only thing left of the cross.
                    float ax = Mathf.Abs(dx);
                    float ay = Mathf.Abs(dy);
                    bool spurH = ay <= thickness * 0.8f && ax > ring * 1.18f && ax < ring * 1.62f;
                    bool spurV = ax <= thickness * 0.8f && ay > ring * 1.18f && ay < ring * 1.62f;
                    if (spurH || spurV) alpha = Mathf.Max(alpha, 0.9f);

                    if (alpha > 0f) lit[py * size + px] = new Color(1f, 0.16f, 0.12f, Mathf.Clamp01(alpha));
                }
            }

            // The reticle texture IS the lit one now - there is no etched layer any more.
            _reticle.SetPixels(lit);
            _reticle.Apply();

            Color[] bloom = Blur(lit, size, 4);
            bloom = Blur(bloom, size, 8);
            for (int i = 0; i < bloom.Length; i++)
                bloom[i] = new Color(1f, 0.22f, 0.16f, Mathf.Clamp01(bloom[i].a * 1.7f));
            _glow.SetPixels(bloom);
            _glow.Apply();
        }

        static Texture2D Blank(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        /// <summary>A separable box blur, run on the alpha. Two passes of this is a perfectly good bloom.</summary>
        static Color[] Blur(Color[] source, int size, int radius)
        {
            Color[] pass = new Color[source.Length];
            Color[] result = new Color[source.Length];
            float weight = 1f / (radius * 2f + 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, size - 1);
                        sum += source[y * size + sx].a;
                    }
                    pass[y * size + x] = new Color(1f, 1f, 1f, sum * weight);
                }
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, size - 1);
                        sum += pass[sy * size + x].a;
                    }
                    result[y * size + x] = new Color(1f, 1f, 1f, sum * weight);
                }
            }
            return result;
        }

        Texture2D _glow;
        public Texture2D Glow { get { EnsureTextures(); return _glow; } }

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
