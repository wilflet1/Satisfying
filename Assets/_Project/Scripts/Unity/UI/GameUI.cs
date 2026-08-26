using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// Menu, HUD and overlays. Also owns cursor lock, because whether the mouse is captured is really
    /// a question of which screen you are on.
    /// </summary>
    public sealed class GameUI
    {
        public NetGame Game;
        public InputBindings Bindings;
        public FeelTuning Feel;
        public TuningPanelUI Tuning;
        public BindingsPanelUI Controls;
        public GearPanelUI Gear;

        /// <summary>Null on desktop. The thumb controls, drawn over the HUD and nothing else.</summary>
        public TouchControlsUI Touch;
        public System.Action OnQuit;

        public bool ShowMenu = true;
        public bool ShowTuning;
        public bool ShowControls;
        public bool ShowGear;

        readonly ScreenGrade _grade = new ScreenGrade();
        UiSkin _skin;
        GUIStyle _centreSmall;
        GUIStyle _centreHeader;
        GUIStyle _centreDim;
        GUIStyle _score;
        GUIStyle _feed;
        GUIStyle _ammo;
        GUIStyle _weaponName;
        LanDiscovery _browser;
        string _name = "duellist";
        string _address = "127.0.0.1";
        string _port = Protocol.DefaultPort.ToString();
        Vector2 _menuScroll;
        float _scale = 1f;
        float _width;
        float _height;

        public UiSkin Skin { get { return _skin; } }

        public void Initialise()
        {
            _skin = UiSkin.Build();
            _name = PlayerPrefs.GetString("satisfying.name", "duellist " + Random.Range(100, 999));
            _address = PlayerPrefs.GetString("satisfying.address", "127.0.0.1");
            _port = PlayerPrefs.GetString("satisfying.port", Protocol.DefaultPort.ToString());
            Tuning.Skin = _skin;
            Controls.Skin = _skin;
            Gear.Skin = _skin;
            BuildDerivedStyles();
            StartBrowsing();
        }

        void StartBrowsing()
        {
            if (_browser != null) return;
            _browser = new LanDiscovery(true);
        }

        void StopBrowsing()
        {
            if (_browser == null) return;
            _browser.Dispose();
            _browser = null;
        }

        public void Shutdown()
        {
            StopBrowsing();
        }

        // ================================================================== per frame
        public void Update(float dt)
        {
            if (_browser != null) _browser.Poll(NetGame.Now());

            if (Bindings.Triggered(GameAction.TuningPanel)) ShowTuning = !ShowTuning;
            if (Bindings.Triggered(GameAction.BindingsPanel)) ShowControls = !ShowControls;
            if (Bindings.Triggered(GameAction.GearPanel)) ShowGear = !ShowGear;
            if (Bindings.Triggered(GameAction.ShowHitboxes))
            {
                Feel.showHitboxes = Feel.showHitboxes >= 0.5f ? 0f : 1f;
                Game.ShowHitboxes = Feel.showHitboxes >= 0.5f;
            }
            if (Bindings.Triggered(GameAction.NetGraph)) Feel.showNetGraph = Feel.showNetGraph > 0.5f ? 0f : 1f;

            if (Bindings.Pressed(GameAction.Menu))
            {
                if (ShowGear) ShowGear = false;
                else if (ShowControls) ShowControls = false;
                else if (ShowTuning) ShowTuning = false;
                else if (Game.InGame) ShowMenu = !ShowMenu;
                else if (Game.CurrentMode != NetGame.Mode.Offline) Game.Leave();   // give up on a connection
            }

            if (!Game.InGame && Game.CurrentMode == NetGame.Mode.Offline)
            {
                ShowMenu = true;
                StartBrowsing();
            }

            // The menu is modal. The tuning and controls panels are not: they free the cursor but leave
            // the keyboard to you, so you can strafe and lean while dragging a slider.
            bool modal = ShowMenu || !Game.InGame;
            bool panelOpen = ShowTuning || ShowControls || ShowGear;
            bool wantsCursor = modal || panelOpen;

            Cursor.lockState = wantsCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = wantsCursor;
            Game.Input.Enabled = !modal && !Controls.Capturing;
            Game.Input.LookEnabled = !wantsCursor;
        }

        // ================================================================== drawing
        public void Draw()
        {
            // A phone is held at arm's length and pressed with a thumb, so everything wants to be
            // bigger than it does on a monitor - buttons especially, since a miss is a lost round.
            float reference = Touch != null ? 520f : 900f;
            _scale = Mathf.Clamp(Screen.height / reference, 0.7f, 3.2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1f));
            _width = Screen.width / _scale;
            _height = Screen.height / _scale;

            bool connecting = !Game.InGame && Game.CurrentMode != NetGame.Mode.Offline;
            if (Game.InGame) DrawHud();
            if (Touch != null && Game.InGame && !ShowMenu && !ShowTuning && !ShowControls && !ShowGear)
            {
                Touch.Skin = _skin;
                Touch.Draw();
            }
            if (connecting && !ShowMenu) DrawConnecting();
            else if (ShowMenu || !Game.InGame) DrawMenu();
            if (ShowTuning) Tuning.Draw(new Rect(_width - 470f, 20f, 450f, _height - 40f));
            if (ShowControls) Controls.Draw(new Rect(20f, 20f, 520f, _height - 40f));
            if (ShowGear) Gear.Draw(new Rect(_width * 0.5f - 250f, _height * 0.5f - 230f, 500f, 460f));
        }

        // ------------------------------------------------------------------ HUD
        void DrawHud()
        {
            NetClient client = Game.Client;
            PlayerSimState state = client.Predicted;
            MovementTuning move = client.Tuning.move;
            WeaponTuning weapon = client.Tuning.Weapon(state.Weapon.Index);

            SightKind sight = (SightKind)Mathf.Clamp(state.Weapon.Sight, 0, 3);

            // The scope goes down before anything else on the HUD, so the hit marker, the kill feed
            // and the ammo count all sit on top of the glass rather than under it.
            ScopeView scope = Game.View != null ? Game.View.Scope : null;
            bool scoped = scope != null && scope.Active;
            if (scoped) scope.Draw(_width, _height, _scale);

            if (state.BlindFire > 0.5f) DrawBlindFireDial(in state, move);
            else if (!scoped)
            {
                DrawCrosshair(in state, move, weapon, sight);
                if (sight != SightKind.Iron && sight != SightKind.Scope)
                    GearPanelUI.DrawReticle(_skin, sight, _width * 0.5f, _height * 0.5f, state.Ads);
            }

            if (scoped) DrawMagnification(scope);
            DrawHitMarker();
            DrawVitals(in state, move, weapon);
            DrawMatchBanner();
            _grade.Strength = Feel.grade;
            _grade.Draw(_width, _height);

            DrawGrenade(in state);
            DrawHill();
            DrawKillFeed();
            DrawStationCaption();
            DrawInteractPrompt(in state, move);

            if (Game.DamageFlashTimer > 0f)
            {
                float k = Game.DamageFlashTimer / 0.35f;
                _skin.Fill(new Rect(0f, 0f, _width, _height), new Color(0.75f, 0.06f, 0.05f, 0.28f * k));
            }

            if (!client.Alive)
            {
                string text = Game.RespawnCountdown > 0.05f
                    ? "respawning in " + Game.RespawnCountdown.ToString("0.0")
                    : "respawning...";
                _skin.Text(new Rect(0f, _height * 0.44f, _width, 40f), text, _centreHeader, UiSkin.Bad);
            }

            if (Feel.showNetGraph > 0.5f) DrawNetGraph();
            if (Feel.showMovementDebug > 0.5f) DrawMovementDebug(in state, move, weapon);
            if (Bindings.Held(GameAction.Scoreboard)) DrawScoreboard();
        }

        /// <summary>OnGUI runs twice a frame; building styles there would allocate for no reason.</summary>
        void BuildDerivedStyles()
        {
            _centreSmall = Centered(_skin.Small);
            _centreHeader = Centered(_skin.Header);
            _centreDim = Centered(_skin.SmallDim);

            _score = new GUIStyle(_skin.Label);
            _score.alignment = TextAnchor.MiddleCenter;
            _score.fontSize = 30;
            _score.fontStyle = FontStyle.Bold;

            _feed = new GUIStyle(_skin.Small);
            _feed.alignment = TextAnchor.MiddleRight;

            _ammo = new GUIStyle(_skin.Label);
            _ammo.alignment = TextAnchor.MiddleRight;
            _ammo.fontSize = 26;

            _weaponName = new GUIStyle(_skin.Small);
            _weaponName.alignment = TextAnchor.MiddleRight;
        }

        static GUIStyle Centered(GUIStyle style)
        {
            GUIStyle copy = new GUIStyle(style);
            copy.alignment = TextAnchor.MiddleCenter;
            return copy;
        }

        void DrawCrosshair(in PlayerSimState state, MovementTuning move, WeaponTuning weapon, SightKind sight)
        {
            // Behind an optic the crosshair is replaced by the reticle; behind irons it just fades out.
            float aimFade = sight == SightKind.Iron ? Mathf.Lerp(0.9f, 0.1f, state.Ads) : Mathf.Lerp(0.9f, 0f, state.Ads * 1.4f);
            if (aimFade <= 0.02f) return;

            float cx = _width * 0.5f;
            float cy = _height * 0.5f;

            float gap = Feel.crosshairGap;
            if (Feel.dynamicCrosshair > 0.5f)
            {
                // Convert the real cone of fire into pixels so the crosshair tells the truth.
                float spread = MovementCore.CurrentSpread(in state, move, weapon, Game.Client.Tuning.Sight(state.Weapon.Sight));
                float halfFov = Game.View.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float pixels = Mathf.Tan(spread * Mathf.Deg2Rad) / Mathf.Max(0.0001f, Mathf.Tan(halfFov)) * (_height * 0.5f);
                gap = Mathf.Clamp(gap + pixels, Feel.crosshairGap, _height * 0.35f);
            }

            float length = Feel.crosshairSize;
            float thickness = Mathf.Max(1f, Feel.crosshairThickness);
            Color color = new Color(1f, 1f, 1f, aimFade);

            _skin.Fill(new Rect(cx - gap - length, cy - thickness * 0.5f, length, thickness), color);
            _skin.Fill(new Rect(cx + gap, cy - thickness * 0.5f, length, thickness), color);
            _skin.Fill(new Rect(cx - thickness * 0.5f, cy - gap - length, thickness, length), color);
            _skin.Fill(new Rect(cx - thickness * 0.5f, cy + gap, thickness, length), color);
            _skin.Fill(new Rect(cx - 1f, cy - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>
        /// Blind firing has no crosshair on purpose - you cannot see what you are shooting at. What you
        /// get instead is the elevation you dialled in, which is the only aim you have.
        /// </summary>
        void DrawBlindFireDial(in PlayerSimState state, MovementTuning move)
        {
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float height = 150f;

            _skin.Fill(new Rect(cx - 2f, cy - height * 0.5f, 4f, height), new Color(0f, 0f, 0f, 0.45f));

            float dial = Mathf.Clamp(state.BlindAngle, -1f, 1f);
            float y = cy - dial * height * 0.5f;
            _skin.Fill(new Rect(cx - 26f, y - 2f, 52f, 4f), UiSkin.Accent);
            _skin.Fill(new Rect(cx - 60f, cy - 1f, 24f, 2f), new Color(1f, 1f, 1f, 0.35f));
            _skin.Fill(new Rect(cx + 36f, cy - 1f, 24f, 2f), new Color(1f, 1f, 1f, 0.35f));

            float elevation = dial >= 0f ? dial * move.blindFirePitchMax : -dial * move.blindFirePitchMin;
            _skin.Text(new Rect(cx + 34f, y - 12f, 90f, 22f), elevation.ToString("0") + "\u00b0", _skin.Small, UiSkin.Accent);
            _skin.Text(new Rect(cx - 90f, cy + height * 0.5f + 6f, 180f, 20f), "BLIND FIRE - wheel aims", _centreDim, UiSkin.InkDim);
        }

        /// <summary>
        /// What the power ring is set to, under the glass. A variable optic you cannot read the
        /// magnification off is a variable optic you will always leave at whatever it was last on.
        /// </summary>
        void DrawMagnification(ScopeView scope)
        {
            float diameter = Mathf.Min(_width, _height) * 0.66f;
            Rect rect = new Rect(_width * 0.5f - 60f * _scale,
                                 _height * 0.5f + diameter * 0.5f + 10f * _scale,
                                 120f * _scale, 22f * _scale);
            GUI.Label(rect, scope.Magnification.ToString("0.0") + "x", _centreDim);
        }

        void DrawHitMarker()
        {
            // The range tells you how far out that one landed - the number people actually want.
            if (Game.LastTargetTimer > 0f)
            {
                float fade = Mathf.Clamp01(Game.LastTargetTimer / 0.6f);
                _skin.Text(new Rect(_width * 0.5f + 26f, _height * 0.5f + 14f, 140f, 18f),
                    Game.LastTargetDistance.ToString("0") + " m", _skin.Small,
                    new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, fade));
            }

            if (Game.HitMarkerTimer <= 0f) return;
            float k = Game.HitMarkerTimer / 0.28f;
            Color color = Game.HitMarkerHeadshot ? new Color(1f, 0.4f, 0.35f, k) : new Color(1f, 1f, 1f, k);
            float cx = _width * 0.5f;
            float cy = _height * 0.5f;
            float inner = 6f;
            float size = 9f;

            for (int i = 0; i < 4; i++)
            {
                float sx = (i == 0 || i == 2) ? -1f : 1f;
                float sy = (i < 2) ? -1f : 1f;
                for (int s = 0; s < (int)size; s++)
                {
                    float x = cx + sx * (inner + s);
                    float y = cy + sy * (inner + s);
                    _skin.Fill(new Rect(x, y, 2f, 2f), color);
                }
            }
        }

        void DrawVitals(in PlayerSimState state, MovementTuning move, WeaponTuning weapon)
        {
            NetClient client = Game.Client;

            // ---------------------------------------------------------- bottom left
            float x = 26f;
            float y = _height - 128f;

            _skin.Text(new Rect(x, y - 20f, 200f, 18f), "HEALTH", _skin.SmallDim, UiSkin.InkDim);
            _skin.Bar(new Rect(x, y, 220f, 12f), client.Health / Mathf.Max(1f, client.Tuning.match.maxHealth),
                Color.Lerp(UiSkin.Bad, UiSkin.Good, client.Health / Mathf.Max(1f, client.Tuning.match.maxHealth)),
                new Color(0f, 0f, 0f, 0.55f));
            _skin.Text(new Rect(x + 228f, y - 4f, 60f, 20f), Mathf.CeilToInt(client.Health).ToString(), _skin.Label, UiSkin.Ink);

            _skin.Text(new Rect(x, y + 20f, 200f, 18f), "STAMINA", _skin.SmallDim, UiSkin.InkDim);
            float stamina01 = state.Stamina / Mathf.Max(1f, move.staminaMax);
            _skin.Bar(new Rect(x, y + 40f, 220f, 8f), stamina01,
                state.Exhausted ? UiSkin.Bad : new Color(0.55f, 0.75f, 0.95f), new Color(0f, 0f, 0f, 0.55f));

            // ---------------------------------------------------------- stance / lean / speed
            float sx = x;
            float sy = y + 62f;
            string stance = state.Stance == Stance.Prone ? "PRONE" : (state.Stance == Stance.Crouch ? "CROUCH" : "STAND");
            if (state.Sliding) stance = "SLIDE";
            else if (state.Vaulting) stance = "VAULT";
            else if (state.Mantling) stance = "MANTLE";
            if (state.BlindFire > 0.5f) stance += "  BLIND";
            _skin.Text(new Rect(sx, sy, 160f, 20f), stance, _skin.Label, UiSkin.Accent);

            _skin.Text(new Rect(sx + 92f, sy + 2f, 60f, 16f), "LEAN", _skin.SmallDim, UiSkin.InkDim);
            _skin.SignedBar(new Rect(sx + 130f, sy + 6f, 90f, 8f), state.Lean,
                new Color(0.75f, 0.78f, 0.85f), new Color(0f, 0f, 0f, 0.55f));

            float dial = Mathf.Lerp(move.speedDialMin, 1f, Game.Input.SpeedDial);
            _skin.Text(new Rect(sx, sy + 22f, 120f, 16f), "SPEED " + Mathf.RoundToInt(dial * 100f) + "%", _skin.SmallDim, UiSkin.InkDim);
            _skin.Bar(new Rect(sx + 92f, sy + 26f, 128f, 6f), Game.Input.SpeedDial, UiSkin.Accent, new Color(0f, 0f, 0f, 0.55f));

            // ---------------------------------------------------------- bottom right
            string ammo = state.Weapon.Reloading ? "RELOADING" : state.Weapon.Ammo + " / " + weapon.MagSizeInt;
            _skin.Text(new Rect(_width - 250f, _height - 84f, 220f, 34f), ammo, _ammo,
                state.Weapon.Reloading ? UiSkin.Accent : UiSkin.Ink);
            _skin.Text(new Rect(_width - 250f, _height - 52f, 220f, 20f), weapon.name.ToUpperInvariant(), _weaponName, UiSkin.InkDim);
        }

        void DrawMatchBanner()
        {
            int mine = 0, theirs = 0;
            foreach (var kv in Game.Players)
            {
                if (kv.Key == Game.Client.PeerId) mine = kv.Value.Kills;
                else theirs = Mathf.Max(theirs, kv.Value.Kills);
            }

            bool leading = mine > theirs;
            Color mineColour = mine == theirs ? UiSkin.Ink : (leading ? UiSkin.Good : UiSkin.Bad);

            string phase;
            switch (Game.Phase)
            {
                case MatchPhase.Warmup: phase = "waiting for an opponent"; break;
                case MatchPhase.Countdown: phase = "starting..."; break;
                case MatchPhase.Ended: phase = ""; break;
                default:
                    phase = Game.PlayingHill
                        ? "hold the room - first to " + Game.Client.Tuning.koth.PointsToWinInt
                        : "first to " + Mathf.RoundToInt(Game.Client.Tuning.match.killsToWin);
                    break;
            }

            _skin.Text(new Rect(_width * 0.5f - 150f, 14f, 140f, 36f), mine.ToString(), _score, mineColour);
            _skin.Text(new Rect(_width * 0.5f - 20f, 14f, 40f, 36f), "-", _score, UiSkin.InkDim);
            _skin.Text(new Rect(_width * 0.5f + 10f, 14f, 140f, 36f), theirs.ToString(), _score, UiSkin.Ink);
            if (phase.Length > 0)
                _skin.Text(new Rect(_width * 0.5f - 200f, 50f, 400f, 20f), phase, _centreSmall, UiSkin.InkDim);

            if (Game.Phase == MatchPhase.Ended) DrawResult(mine, theirs);
        }

        /// <summary>
        /// The end of a duel, said properly. It used to be twenty pixels of dim grey text tucked under
        /// the score, which is a strange way to tell someone they just won - you could finish a match
        /// and not notice. Now it takes the middle of the screen.
        /// </summary>
        void DrawResult(int mine, int theirs)
        {
            bool won = Game.Winner == Game.Client.PeerId;
            Color colour = won ? UiSkin.Good : UiSkin.Bad;

            // A wash over the whole screen in the result's colour, and a band behind the word.
            GUI.color = new Color(colour.r, colour.g, colour.b, 0.10f);
            GUI.DrawTexture(new Rect(0f, 0f, _width, _height), _skin.White);

            float bandHeight = 132f;
            float bandY = _height * 0.36f;
            GUI.color = new Color(0.05f, 0.055f, 0.07f, 0.88f);
            GUI.DrawTexture(new Rect(0f, bandY, _width, bandHeight), _skin.White);
            GUI.color = new Color(colour.r, colour.g, colour.b, 0.95f);
            GUI.DrawTexture(new Rect(0f, bandY, _width, 3f), _skin.White);
            GUI.DrawTexture(new Rect(0f, bandY + bandHeight - 3f, _width, 3f), _skin.White);
            GUI.color = Color.white;

            _skin.Text(new Rect(0f, bandY + 20f, _width, 60f), won ? "VICTORY" : "DEFEAT", _centreHeader, colour);
            _skin.Text(new Rect(0f, bandY + 84f, _width, 28f),
                       mine + " - " + theirs + "     " +
                       (won ? (Game.PlayingHill ? "the house is yours" : "the duel is yours")
                            : "better luck next round"),
                       _centreSmall, UiSkin.InkDim);
        }

        /// <summary>
        /// What is in your hand and how many are left. The state matters more than the count: once
        /// the pin is out the only ways back are throwing it or dying with it, and that deserves to
        /// be said on the screen rather than remembered.
        /// </summary>
        void DrawGrenade(in PlayerSimState state)
        {
            GrenadeTuning tuning = Game.Client.Tuning.grenade;
            float x = _width * 0.5f - 150f;
            float y = _height - 96f;

            if (state.Carry == GrenadeCarry.Stowed)
            {
                if (state.GrenadesLeft == 0) return;
                _skin.Text(new Rect(x, y, 300f, 18f),
                           "grenades  " + state.GrenadesLeft, _centreSmall, UiSkin.InkDim);
                return;
            }

            string caption;
            Color colour;
            if (state.Carry == GrenadeCarry.Drawing)
            {
                caption = "PULLING THE PIN";
                colour = UiSkin.Accent;
            }
            else
            {
                caption = "LEFT throw   RIGHT lob";
                colour = UiSkin.Bad;
            }

            GUI.color = new Color(0.05f, 0.055f, 0.07f, 0.8f);
            GUI.DrawTexture(new Rect(x, y - 4f, 300f, 40f), _skin.White);
            GUI.color = Color.white;
            _skin.Text(new Rect(x, y, 300f, 18f), caption, _centreSmall, colour);

            if (state.Carry == GrenadeCarry.Drawing)
            {
                float k = 1f - Mathf.Clamp01(state.CarryTimer / Mathf.Max(0.05f, tuning.drawTime));
                GUI.color = new Color(1f, 1f, 1f, 0.14f);
                GUI.DrawTexture(new Rect(x + 60f, y + 22f, 180f, 3f), _skin.White);
                GUI.color = new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.95f);
                GUI.DrawTexture(new Rect(x + 60f, y + 22f, 180f * k, 3f), _skin.White);
                GUI.color = Color.white;
            }
            else
            {
                _skin.Text(new Rect(x, y + 18f, 300f, 16f),
                           "no cook - the fuse starts when it leaves your hand", _centreSmall, UiSkin.InkDim);
            }
        }

        /// <summary>
        /// The hill: which room, how long it has, and whether you are in it.
        ///
        /// It sits under the score rather than in a corner, because it is the thing the mode is
        /// about - if you have to go looking for it you will play the map instead of the mode. The
        /// bar runs down as the room's time does, and goes amber over the last few seconds so the
        /// move is something you can prepare for rather than something that happens to you.
        /// </summary>
        void DrawHill()
        {
            if (!Game.PlayingHill || Game.ActiveZone < 0) return;

            KothTuning koth = Game.Client.Tuning.koth;
            bool mine = Game.ZoneHolder == Game.Client.PeerId;
            bool contested = Game.ZoneHolder == KothState.Contested;
            bool warning = Game.ZoneTimer <= koth.warnSeconds;

            Color colour = contested ? UiSkin.Accent : (mine ? UiSkin.Good : (Game.ZoneHolder >= 0 ? UiSkin.Bad : UiSkin.InkDim));

            float width = 300f;
            float x = _width * 0.5f - width * 0.5f;
            float y = 76f;

            GUI.color = new Color(0.05f, 0.055f, 0.07f, 0.78f);
            GUI.DrawTexture(new Rect(x, y, width, 46f), _skin.White);
            GUI.color = new Color(colour.r, colour.g, colour.b, 0.95f);
            GUI.DrawTexture(new Rect(x, y, 3f, 46f), _skin.White);
            GUI.color = Color.white;

            string holder = contested ? "CONTESTED" : mine ? "YOU HOLD IT"
                          : Game.ZoneHolder >= 0 ? "THEY HOLD IT" : "OPEN";
            _skin.Text(new Rect(x + 12f, y + 4f, width - 24f, 20f), Game.ZoneName(Game.ActiveZone).ToUpperInvariant(),
                       _centreSmall, UiSkin.Ink);
            _skin.Text(new Rect(x + 12f, y + 22f, width - 24f, 16f), holder, _centreSmall, colour);

            // The clock, as a bar that empties.
            float span = Mathf.Max(1f, koth.rotateSeconds);
            float k = Mathf.Clamp01(Game.ZoneTimer / span);
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            GUI.DrawTexture(new Rect(x + 12f, y + 40f, width - 24f, 3f), _skin.White);
            GUI.color = warning ? new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.95f)
                                : new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(x + 12f, y + 40f, (width - 24f) * k, 3f), _skin.White);
            GUI.color = Color.white;

            if (warning)
                _skin.Text(new Rect(x, y + 48f, width, 16f),
                           "moving in " + Mathf.CeilToInt(Game.ZoneTimer) + "s", _centreSmall, UiSkin.Accent);
        }

        /// <summary>
        /// Who killed whom, in a form you can read without stopping to read it.
        ///
        /// It used to be one dim grey line of running text at 18 px, which in a firefight is
        /// indistinguishable from not being there. Now each entry is a plate with the two names in
        /// colour - yours in the accent, theirs in red - the zone called out where it matters, and
        /// anything you did or that was done to you outlined so it catches the eye.
        /// </summary>
        void DrawKillFeed()
        {
            float rowHeight = 26f;
            float width = 330f;
            float y = 92f;

            for (int i = Game.KillFeed.Count - 1; i >= 0; i--)
            {
                NetGame.KillFeedEntry entry = Game.KillFeed[i];
                float age = Time.time - entry.Time;
                float alpha = Mathf.Clamp01(2.4f - age * 0.4f);
                if (alpha <= 0.01f) continue;

                bool mineKill = Game.Client != null && entry.KillerId == Game.Client.PeerId;
                bool myDeath = Game.Client != null && entry.VictimId == Game.Client.PeerId;
                bool involved = mineKill || myDeath;

                Rect row = new Rect(_width - width - 24f, y, width, rowHeight - 4f);

                // A plate behind it, brighter when it was you at either end.
                GUI.color = new Color(0.05f, 0.055f, 0.07f, alpha * (involved ? 0.82f : 0.55f));
                GUI.DrawTexture(row, _skin.White);
                if (involved)
                {
                    GUI.color = new Color(mineKill ? UiSkin.Accent.r : UiSkin.Bad.r,
                                          mineKill ? UiSkin.Accent.g : UiSkin.Bad.g,
                                          mineKill ? UiSkin.Accent.b : UiSkin.Bad.b, alpha * 0.9f);
                    GUI.DrawTexture(new Rect(row.x, row.y, 3f, row.height), _skin.White);
                }
                GUI.color = Color.white;

                Color killerColour = mineKill ? UiSkin.Accent : UiSkin.Ink;
                Color victimColour = myDeath ? UiSkin.Bad : UiSkin.Ink;

                float x = row.x + 10f;
                x += Segment(entry.Killer, x, row.y, killerColour, alpha, _feed);
                x += Segment("  >  ", x, row.y, UiSkin.InkDim, alpha, _feed);
                x += Segment(entry.Victim, x, row.y, victimColour, alpha, _feed);

                string zone = ZoneLabel(entry.Zone);
                if (zone.Length > 0)
                    Segment(zone, x, row.y, entry.Zone == HitZone.Head ? UiSkin.Accent : UiSkin.InkDim,
                            alpha, _feed);

                y += rowHeight;
            }
        }

        /// <summary>Draws one run of coloured text and returns how wide it was, so the next one can
        /// start where it finished.</summary>
        float Segment(string text, float x, float y, Color colour, float alpha, GUIStyle style)
        {
            float width = style.CalcSize(new GUIContent(text)).x;
            _skin.Text(new Rect(x, y, width + 4f, 22f), text, style,
                       new Color(colour.r, colour.g, colour.b, alpha));
            return width;
        }

        /// <summary>
        /// Where it landed. The chest is not called out because it is the ordinary case, and a feed that
        /// labels everything labels nothing.
        /// </summary>
        static string ZoneLabel(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return "  [head]";
                case HitZone.Neck: return "  [neck]";
                case HitZone.Stomach: return "  [stomach]";
                case HitZone.Arm: return "  [arm]";
                case HitZone.Leg: return "  [leg]";
                case HitZone.Foot: return "  [foot]";
                default: return "";
            }
        }

        /// <summary>What the grab key would do right now, and what it is costing you.</summary>
        /// <summary>
        /// While hosting, the one thing anyone actually needs: the address to give out, and whether
        /// the router agreed to forward the port. Copyable, because reading an IP aloud is miserable.
        /// </summary>
        void DrawHostingAddress()
        {
            // The attempts line matters whether or not the port mapper ran, so this panel is drawn
            // for any host, not only one that tried UPnP.
            PortMapper mapper = Game.Mapper;
            ReachabilityProbe probe = Game.Reachability;
            if (mapper == null && probe == null && Game.Server == null) return;

            GUILayout.BeginVertical(_skin.PanelDim);

            // Two independent sources, and they disagree in the case that matters: a port forwarded by
            // hand on the router leaves UPnP reporting failure while the door is wide open. The probe
            // asks the outside world, so it is the one that gets to say whether anyone can get in.
            bool mapped = mapper != null && mapper.State == PortMapper.Result.Mapped;
            bool confirmed = probe != null && probe.State == ReachabilityProbe.Verdict.Confirmed;
            bool looksOpen = probe != null && probe.State == ReachabilityProbe.Verdict.PortPreserved;
            bool definitelyShut = probe != null && probe.State == ReachabilityProbe.Verdict.PortRemapped;

            // Only proof silences the advice. A preserved port is NOT proof: a NAT with no forward at all
            // usually preserves the port too, so treating it as open would swap one lie for another.
            bool proven = confirmed || mapped;

            Color previous = GUI.color;
            if (mapper != null)
            {
                GUI.color = mapped ? UiSkin.Good : (mapper.State == PortMapper.Result.Failed ? UiSkin.Bad : UiSkin.Ink);
                GUILayout.Label(mapper.Status, _skin.Small);
            }

            if (probe != null)
            {
                GUI.color = confirmed ? UiSkin.Good
                    : definitelyShut || probe.State == ReachabilityProbe.Verdict.NoAnswer ? UiSkin.Bad
                    : looksOpen ? UiSkin.Ink : UiSkin.InkDim;
                GUILayout.Label(probe.Describe(Game.Port), _skin.Small);
            }
            GUI.color = previous;

            // Neither of those can tell you whether anything is actually turning up. This can: it
            // splits "they never reached me" from "my reply never reached them", which are different
            // problems with different fixes.
            NetServer server = Game.Server;
            if (server != null)
            {
                GUILayout.Label(server.ConnectAttemptsSeen == 0
                        ? "no connection attempts have reached this machine yet"
                        : server.ConnectAttemptsSeen + " connection attempt(s) arrived - last one " + server.LastConnectResult,
                    _skin.Small);
            }

            // The address to hand out. STUN knows it even when UPnP failed, which is exactly the case
            // where the host most needs to be told what to type into the chat window.
            string external = null;
            if (probe != null && !string.IsNullOrEmpty(probe.ExternalAddress) && !definitelyShut)
                external = probe.ExternalAddress + ":" + Game.Port;
            else if (mapped && !string.IsNullOrEmpty(mapper.ExternalAddress))
                external = mapper.ExternalAddress + ":" + Game.Port;

            if (external != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(confirmed ? "anyone can join at" : "try giving out", _skin.Small, GUILayout.Width(130f));
                GUILayout.Label(external, _skin.Value);
                if (GUILayout.Button("copy", _skin.ButtonSmall, GUILayout.Width(52f)))
                    GUIUtility.systemCopyBuffer = external;
                GUILayout.EndHorizontal();
            }

            if (!proven)
            {
                GUILayout.Label("forward UDP " + Game.Port + " to " + UdpTransport.LocalAddress() +
                                " on your router, or run a dedicated server - see docs/SERVER.md",
                                _skin.SmallDim);
            }

            GUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        void DrawInteractPrompt(in PlayerSimState state, MovementTuning move)
        {
            NetClient client = Game.Client;
            string key = Bindings[GameAction.Grab].ToString();

            if (Game.HeldProp >= 0)
            {
                float mass = Game.Scenery != null ? Game.Scenery.PropMass(Game.HeldProp) : state.CarryMass;
                _skin.Text(new Rect(0f, _height * 0.58f, _width, 22f),
                    key + "  drop     " + Mathf.RoundToInt(mass) + " kg", _centreSmall, UiSkin.Accent);
                return;
            }

            if (!client.Alive) return;
            int target = PropSim.FindGrabbable(client.PeerId, in state, move, client.Model, client.World);
            if (target < 0) return;

            float targetMass = Game.Scenery != null ? Game.Scenery.PropMass(target) : 0f;
            _skin.Text(new Rect(0f, _height * 0.58f, _width, 22f),
                key + "  drag     " + Mathf.RoundToInt(targetMass) + " kg", _centreSmall, UiSkin.Ink);
        }

        /// <summary>On the test range, name whichever drill you are standing in.</summary>
        void DrawStationCaption()
        {
            if (Game.Stations == null || Game.Stations.Count == 0) return;

            Vector3 me = Game.Client.RenderPosition.ToUnity();
            float best = 16f;
            int nearest = -1;
            for (int i = 0; i < Game.Stations.Count; i++)
            {
                float distance = Vector3.Distance(me, Game.Stations[i].Position);
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            if (nearest < 0) return;

            ArenaBuilder.Station station = Game.Stations[nearest];
            float fade = Mathf.Clamp01((16f - best) / 4f);
            _skin.Text(new Rect(0f, _height - 172f, _width, 24f), station.Title, _centreHeader,
                new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, fade));
            _skin.Text(new Rect(0f, _height - 150f, _width, 20f), station.Hint, _centreDim,
                new Color(UiSkin.InkDim.r, UiSkin.InkDim.g, UiSkin.InkDim.b, fade));
        }

        void DrawNetGraph()
        {
            NetClient c = Game.Client;
            GUILayout.BeginArea(new Rect(_width - 250f, _height - 250f, 230f, 190f), _skin.PanelDim);
            GUILayout.Label("NETWORK", _skin.Header);
            Row("ping", Mathf.RoundToInt(c.Rtt * 1000f) + " ms");
            Row("jitter", Mathf.RoundToInt(c.Jitter * 1000f) + " ms");
            Row("tick", c.ClientTick + " / " + c.ServerTick);
            Row("input buffer", c.BufferHealth.ToString());
            Row("corrections", c.Corrections + "  (" + (c.LastCorrectionError * 100f).ToString("0.0") + " cm)" +
                               (c.HistoryMisses > 0 ? "   " + c.HistoryMisses + " past the buffer" : ""));
            Row("down / up", (c.BytesInPerSecond / 1024f).ToString("0.0") + " / " + (c.BytesOutPerSecond / 1024f).ToString("0.0") + " KB/s");
            Row("interp", Mathf.RoundToInt(c.NetTuning.interpolationDelayMs) + " ms");
            GUILayout.EndArea();
        }

        void DrawMovementDebug(in PlayerSimState state, MovementTuning move, WeaponTuning weapon)
        {
            GUILayout.BeginArea(new Rect(26f, 90f, 250f, 248f), _skin.PanelDim);
            GUILayout.Label("MOVEMENT", _skin.Header);
            Row("speed", state.Velocity.Flat.Magnitude.ToString("0.00") + " m/s");
            Row("vertical", state.Velocity.y.ToString("0.00"));
            Row("grounded", state.Grounded ? "yes" : "no");
            Row("height", state.Height.ToString("0.00"));
            Row("lean", state.Lean.ToString("0.00") + " -> " + state.EffectiveLean(move).ToString("0.00"));
            Row("side step", state.SideStep.ToString("0.00"));
            Row("carrying", state.CarryMass > 0f ? Mathf.RoundToInt(state.CarryMass) + " kg" : "-");
            Row("slide", state.Sliding ? state.SlideTimer.ToString("0.00") + "s left"
                : (state.SlideCooldown > 0f ? "cooldown " + state.SlideCooldown.ToString("0.00") : "ready"));
            Row("stamina", state.Stamina.ToString("0") + (state.Exhausted ? "  winded" : ""));

            // The cone of fire as a number, and what it means downrange, so spread can be tuned by
            // reading rather than by guessing at the crosshair.
            float cone = MovementCore.CurrentSpread(in state, move, weapon, Game.Client.Tuning.Sight(state.Weapon.Sight));
            Row("spread", cone.ToString("0.00") + " deg");
            Row("at 25 m", (Mathf.Tan(cone * Mathf.Deg2Rad) * 25f * 200f).ToString("0") + " cm group");
            GUILayout.EndArea();
        }

        void Row(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _skin.SmallDim);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, _skin.Small);
            GUILayout.EndHorizontal();
        }

        void DrawScoreboard()
        {
            float w = 460f;
            float h = 60f + Game.Players.Count * 26f;
            GUILayout.BeginArea(new Rect(_width * 0.5f - w * 0.5f, _height * 0.25f, w, h), _skin.Panel);
            GUILayout.Label("DUEL", _skin.Header);
            foreach (var kv in Game.Players)
            {
                GUILayout.BeginHorizontal();
                bool me = kv.Key == Game.Client.PeerId;
                GUILayout.Label(kv.Value.Name + (me ? "  (you)" : ""), me ? _skin.Value : _skin.Label);
                GUILayout.FlexibleSpace();
                GUILayout.Label(kv.Value.Kills + " / " + kv.Value.Deaths, _skin.Label, GUILayout.Width(70f));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Say what is actually happening. A spinner that never resolves tells you nothing; the number
        /// of unanswered requests and how long they have gone unanswered tells you the server is not
        /// replying, which is a different problem from the server refusing you.
        /// </summary>
        void DrawConnecting()
        {
            NetClient client = Game.Client;
            _skin.Fill(new Rect(0f, 0f, _width, _height), new Color(0.03f, 0.035f, 0.05f, 0.85f));
            _skin.Text(new Rect(0f, _height * 0.44f, _width, 40f), "connecting...", _centreHeader, UiSkin.Accent);

            if (client != null)
            {
                _skin.Text(new Rect(0f, _height * 0.50f, _width, 24f),
                    client.ConnectAttempts + " request" + (client.ConnectAttempts == 1 ? "" : "s") +
                    " sent, no reply yet   (" + client.ConnectingFor.ToString("0.0") + "s)",
                    _centreDim, UiSkin.InkDim);

                if (client.ConnectingFor > 4f)
                {
                    _skin.Text(new Rect(0f, _height * 0.55f, _width, 24f),
                        "nothing is coming back from " + _address + " - check the address, the port forward, and their firewall",
                        _centreDim, UiSkin.Bad);
                }
            }

            _skin.Text(new Rect(0f, _height * 0.61f, _width, 24f), "Esc to give up", _centreDim, UiSkin.InkDim);
        }

        // ------------------------------------------------------------------ menu
        void DrawMenu()
        {
            _skin.Fill(new Rect(0f, 0f, _width, _height), new Color(0.03f, 0.035f, 0.05f, Game.InGame ? 0.72f : 0.97f));

            float w = 520f;
            float h = Mathf.Min(_height - 60f, 640f);
            Rect area = new Rect(_width * 0.5f - w * 0.5f, _height * 0.5f - h * 0.5f, w, h);

            GUILayout.BeginArea(area, _skin.Panel);
            GUILayout.Label("SATISFYING", _skin.Title);
            GUILayout.Label("a 1v1 movement duel - lean, slow lean, prone lean, side step, blind fire", _centreDim);
            GUILayout.Label(BuildStamp.Describe(Protocol.Version), _skin.SmallDim);
            GUILayout.Space(10f);

            _menuScroll = GUILayout.BeginScrollView(_menuScroll);

            if (!string.IsNullOrEmpty(Game.Status))
            {
                GUILayout.Label(Game.Status, _skin.SmallDim);
                GUILayout.Space(4f);
            }

            DrawHostingAddress();

            GUILayout.BeginHorizontal();
            GUILayout.Label("name", _skin.Label, GUILayout.Width(90f));
            _name = GUILayout.TextField(_name, 20, _skin.TextField);
            GUILayout.EndHorizontal();

            if (Game.InGame)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button("resume", _skin.ButtonPrimary, GUILayout.Height(38f))) ShowMenu = false;
                if (GUILayout.Button("leave match", _skin.Button, GUILayout.Height(32f)))
                {
                    Game.Leave();
                    ShowMenu = true;
                }

                if (Game.IsHost)
                {
                    GUILayout.Space(10f);
                    GUILayout.Label("PRACTICE", _skin.Header);
                    GUILayout.Label("A training bot is a real player to the server - it moves, leans, takes cover and shoots back.", _skin.SmallDim);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("add a training bot", _skin.Button))
                    {
                        Game.Server.AddBot("training bot", 0.55f);
                        ShowMenu = false;
                    }
                    if (Game.Server.BotCount > 0 && GUILayout.Button("remove bots", _skin.Button))
                        Game.Server.RemoveBots();
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Space(8f);
                GUILayout.Label("HOST", _skin.Header);

                GUILayout.BeginHorizontal();
                GUILayout.Label("map", _skin.Label, GUILayout.Width(90f));
                if (GUILayout.Button(Game.HostMap == MapId.DuelArena ? "> duel arena" : "duel arena",
                        Game.HostMap == MapId.DuelArena ? _skin.ButtonPrimary : _skin.Button))
                    Game.HostMap = MapId.DuelArena;
                if (GUILayout.Button(Game.HostMap == MapId.House ? "> the house" : "the house",
                        Game.HostMap == MapId.House ? _skin.ButtonPrimary : _skin.Button))
                    Game.HostMap = MapId.House;
                if (GUILayout.Button(Game.HostMap == MapId.TestRange ? "> test range" : "test range",
                        Game.HostMap == MapId.TestRange ? _skin.ButtonPrimary : _skin.Button))
                    Game.HostMap = MapId.TestRange;
                GUILayout.EndHorizontal();
                GUILayout.Label(Game.HostMap == MapId.TestRange
                    ? "A drill course: vault row, slide lane, mantle stack, lean gallery, shooting range."
                    : Game.HostMap == MapId.House
                        ? "Two floors and a yard. Stairs, a landing window off the porch roof and a hole "
                          + "in the bedroom floor, so nobody owns the building by holding one of them. The "
                          + "wall between the kitchen and the living room is plasterboard."
                        : "The duel map: corners, window sills, a roof and one long sightline.", _skin.SmallDim);

                GUILayout.Space(4f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("mode", _skin.Label, GUILayout.Width(90f));
                if (GUILayout.Button(Game.HostMode == GameMode.Duel ? "> duel" : "duel",
                        Game.HostMode == GameMode.Duel ? _skin.ButtonPrimary : _skin.Button))
                    Game.HostMode = GameMode.Duel;
                if (GUILayout.Button(Game.HostMode == GameMode.KingOfTheHill ? "> king of the hill" : "king of the hill",
                        Game.HostMode == GameMode.KingOfTheHill ? _skin.ButtonPrimary : _skin.Button))
                    Game.HostMode = GameMode.KingOfTheHill;
                GUILayout.EndHorizontal();
                GUILayout.Label(Game.HostMode == GameMode.KingOfTheHill
                    ? "One room is the hill. Hold it alone to score; both of you in it stops the clock. "
                      + "It moves to another room every minute, and only the house has rooms to move between."
                    : "First to the kill count wins.", _skin.SmallDim);

                GUILayout.BeginHorizontal();
                GUILayout.Label("port", _skin.Label, GUILayout.Width(90f));
                _port = GUILayout.TextField(_port, 6, _skin.TextField, GUILayout.Width(110f));
                GUILayout.EndHorizontal();
                if (GUILayout.Button("host a duel", _skin.ButtonPrimary, GUILayout.Height(38f))) DoHost();

                GUILayout.Space(10f);
                GUILayout.Label("JOIN", _skin.Header);
                GUILayout.BeginHorizontal();
                GUILayout.Label("address", _skin.Label, GUILayout.Width(90f));
                _address = GUILayout.TextField(_address, 40, _skin.TextField);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("join", _skin.Button, GUILayout.Height(32f))) DoJoin(_address);

                if (_browser != null && _browser.Servers.Count > 0)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label("on your network", _skin.SmallDim);
                    for (int i = 0; i < _browser.Servers.Count; i++)
                    {
                        LanDiscovery.Found found = _browser.Servers[i];
                        if (!GUILayout.Button(found.Name + "   " + found.Address + ":" + found.Port, _skin.ButtonSmall)) continue;
                        _address = found.Address;
                        _port = found.Port.ToString();
                        DoJoin(found.Address);
                    }
                }
            }

            GUILayout.Space(12f);
            GUILayout.Label("NETWORK SIMULATOR", _skin.Header);
            GUILayout.Label("Adds delay and loss to what THIS machine sends. Run it on both ends to feel a real connection.", _skin.SmallDim);
            DrawConditions(Game.Conditions);

            GUILayout.Space(12f);
            GUILayout.Label("CONTROLS", _skin.Header);
            GUILayout.Label(
                "WASD move   Space jump/mantle   Shift sprint   C crouch   X prone\n" +
                "Q / E lean   Alt+Q / Alt+E slow lean (move the mouse for a fine lean)\n" +
                "Alt+A / Alt+D side step   Ctrl walk   wheel speed dial\n" +
                "sprint + tap C to slide   Space at a railing to vault it\n" +
                "V melee with the stock (breaks glass)   F grab and drag objects\n" +
                "B blind fire (wheel aims it)   1 M4A1   2 MP5   3 USP45\n" +
                "G gear   F1 tuning   F2 controls   F3 net graph   Tab scoreboard   Esc menu",
                _skin.Small);

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("controls (F2)", _skin.Button)) ShowControls = !ShowControls;
            if (GUILayout.Button("tuning (F1)", _skin.Button)) ShowTuning = !ShowTuning;
            GUILayout.EndHorizontal();

            if (!Game.InGame && GUILayout.Button("quit", _skin.Button))
            {
                if (OnQuit != null) OnQuit();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawConditions(NetConditions conditions)
        {
            conditions.latencyMs = LabelledSlider("latency (one way)", conditions.latencyMs, 0f, 300f, "0 ms");
            conditions.jitterMs = LabelledSlider("jitter", conditions.jitterMs, 0f, 100f, "0 ms");
            conditions.lossPercent = LabelledSlider("packet loss", conditions.lossPercent, 0f, 30f, "0 %");
        }

        float LabelledSlider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _skin.Small, GUILayout.Width(150f));
            float result = GUILayout.HorizontalSlider(value, min, max, _skin.Slider, _skin.SliderThumb);
            GUILayout.Label(result.ToString(format), _skin.Value, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            return result;
        }

        void DoHost()
        {
            int port;
            if (!int.TryParse(_port, out port)) port = Protocol.DefaultPort;
            SavePrefs();
            StopBrowsing();
            string error;
            if (Game.Host(port, _name, out error)) ShowMenu = false;
        }

        void DoJoin(string address)
        {
            int port;
            if (!int.TryParse(_port, out port)) port = Protocol.DefaultPort;

            // The host is handed "1.2.3.4:7777" to give out, so that is what arrives in this box.
            // Take the port from the address when it carries one, and tolerate a pasted link.
            string host;
            int fromAddress;
            if (NetAddress.TryParse(address, port, out host, out fromAddress))
            {
                address = host;
                port = fromAddress;
                _port = port.ToString();
                _address = host;
            }

            SavePrefs();
            StopBrowsing();
            string error;
            if (Game.Join(address, port, _name, out error)) ShowMenu = false;
        }

        void SavePrefs()
        {
            PlayerPrefs.SetString("satisfying.name", _name);
            PlayerPrefs.SetString("satisfying.address", _address);
            PlayerPrefs.SetString("satisfying.port", _port);
            PlayerPrefs.Save();
        }
    }
}
