using UnityEngine;
using Satisfying.Shared;

namespace Satisfying.Game
{
    /// <summary>
    /// The whole game in one component. It builds the arena, the camera rig, the audio and the UI at
    /// runtime, so the project contains no scenes, prefabs or binary assets that could go stale:
    /// open the project, press play.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const int LayerWorld = 8;
        public const int LayerPlayer = 9;
        public const int LayerProbe = 10;
        public const int LayerViewmodel = 11;
        public const int LayerFx = 12;

        public static GameBootstrap Instance { get; private set; }

        NetGame _game;
        GameUI _ui;
        TuningPanelUI _tuningPanel;
        BindingsPanelUI _bindingsPanel;
        GearPanelUI _gearPanel;
        InputBindings _bindings;
        FeelTuning _feel;
        LocalInputSource _input;
        PlayerView _view;
        UnityCollisionWorld _world;
        Palette _palette;
        AudioBank _audio;
        SoundPlayer _sound;
        CombatFx _fx;
        float _pushTuningTimer;
        bool _tuningDirty;
        GameObject _arenaRoot;
        readonly SpawnSet _spawns = new SpawnSet();
        readonly WorldModel _worldModel = new WorldModel();
        WorldView _scenery;

        /// <summary>
        /// Boots the game from any scene, including an empty one. Unity projects usually hide their
        /// entry point inside a .unity file; keeping it in code means nothing can come unwired.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Instance != null) return;
#if UNITY_2023_1_OR_NEWER
            GameBootstrap existing = Object.FindFirstObjectByType<GameBootstrap>();
#else
            GameBootstrap existing = Object.FindObjectOfType<GameBootstrap>();
#endif
            if (existing != null) return;

            GameObject go = new GameObject("Satisfying");
            go.AddComponent<GameBootstrap>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ConfigureEngine();
            BuildWorld();
            BuildPlayerSystems();
            BuildInterface();
            TakeOverSceneCameras();
            ApplyCommandLine();
        }

        /// <summary>
        /// The game brings its own camera and audio listener. If you press play in a scene that already
        /// has Unity's default ones, stand them down rather than fighting over which one renders.
        /// </summary>
        void TakeOverSceneCameras()
        {
#if UNITY_2023_1_OR_NEWER
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
#else
            Camera[] cameras = Object.FindObjectsOfType<Camera>();
            AudioListener[] listeners = Object.FindObjectsOfType<AudioListener>();
#endif
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] == null || cameras[i].transform.IsChildOf(transform)) continue;
                cameras[i].enabled = false;
            }
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] == null || listeners[i].transform.IsChildOf(transform)) continue;
                listeners[i].enabled = false;
            }
        }

        /// <summary>
        /// Lets a second instance join automatically, which is how you playtest netcode alone:
        ///   Satisfying -connect 127.0.0.1 -name challenger
        ///   Satisfying -host -port 7777
        /// </summary>
        void ApplyCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            string connect = null;
            string playerName = null;
            int port = Protocol.DefaultPort;
            bool host = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-host": host = true; break;
                    case "-connect": if (i + 1 < args.Length) connect = args[++i]; break;
                    case "-name": if (i + 1 < args.Length) playerName = args[++i]; break;
                    case "-port":
                        if (i + 1 < args.Length && !int.TryParse(args[++i], out port)) port = Protocol.DefaultPort;
                        break;
                }
            }

            if (!host && connect == null) return;
            if (playerName == null) playerName = host ? "host" : "challenger";

            string error;
            bool started = host ? _game.Host(port, playerName, out error) : _game.Join(connect, port, playerName, out error);
            if (started) _ui.ShowMenu = false;
            else Debug.LogWarning("[satisfying] command line start failed: " + error);
        }

        void ConfigureEngine()
        {
            Application.targetFrameRate = -1;
            Application.runInBackground = true;   // two instances on one machine must both keep ticking
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = Protocol.TickDt;

            // The query probe capsule must never take part in the physics simulation.
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerProbe, i, true);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerPlayer, i, true);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerFx, i, true);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerViewmodel, i, true);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.36f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.23f, 0.27f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.11f, 0.13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.14f, 0.16f, 0.2f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 220f;
        }

        void BuildWorld()
        {
            _palette = Palette.Build();

            GameObject sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            sunGo.transform.rotation = Quaternion.Euler(48f, 36f, 0f);
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.87f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;

            _world = new UnityCollisionWorld(1 << LayerWorld, LayerProbe);

            _audio = AudioBank.Build();
            _sound = new SoundPlayer(transform);
            _fx = new CombatFx(transform, _palette, LayerFx);

            _game = new NetGame();
            _game.World = _world;
            _game.Spawns = _spawns;
            _game.Model = _worldModel;
            _game.OnMapRequested = BuildArena;
            _scenery = new WorldView();
            _game.Scenery = _scenery;
            _game.Palette = _palette;
            _game.Audio = _audio;
            _game.Sound = _sound;
            _game.Fx = _fx;
            _game.Root = transform;
            _game.PlayerLayer = LayerPlayer;

            BuildArena(MapId.DuelArena);
        }

        /// <summary>
        /// Swaps the arena at runtime. The old geometry is deactivated before it is destroyed, because
        /// Destroy is deferred to the end of the frame and two maps of colliders would overlap for it.
        /// </summary>
        public void BuildArena(MapId map)
        {
            if (_arenaRoot != null)
            {
                _arenaRoot.SetActive(false);
                Destroy(_arenaRoot);
            }

            ArenaBuilder.Result arena = ArenaBuilder.Build(map, _spawns, _palette, LayerWorld, _worldModel);
            arena.Root.transform.SetParent(transform, false);
            _arenaRoot = arena.Root;

            // Glass and movable objects come from the shared model, so both machines agree on them.
            _scenery.Build(_worldModel, _palette, LayerWorld, arena.Root.transform, _fx, _sound, _audio);
            if (_game.Client != null) _game.Client.ResetWorld();

            _game.CurrentMap = map;
            _game.Stations = arena.Stations;
        }

        void BuildPlayerSystems()
        {
            _feel = new FeelTuning();
            _bindings = new InputBindings();
            _bindings.Load();

            _input = new LocalInputSource();
            _input.Bindings = _bindings;
            _input.Feel = _feel;
            _input.Tuning = _game.Tuning;

            _view = new PlayerView(transform, _feel, _palette, LayerViewmodel);
            // Menu backdrop. Pose the rig, not the camera: the camera sits at the rig's origin and the
            // view code drives the rig, so moving the camera itself would leave a permanent offset.
            _view.Rig.position = new Vector3(0f, 6f, -18f);
            _view.Rig.rotation = Quaternion.Euler(12f, 0f, 0f);

            _game.Feel = _feel;
            _game.Input = _input;
            _game.View = _view;

            // Sound is tested against the same geometry bullets are, so a broken window opens a
            // listening path at the moment it opens a firing one.
            _sound.Listener = _view.Camera.transform;
            _sound.OcclusionMask = 1 << LayerWorld;
            _sound.MasterVolume = _feel.masterVolume;
        }

        void BuildInterface()
        {
            _tuningPanel = new TuningPanelUI();
            _tuningPanel.Game = _game;
            _tuningPanel.Feel = _feel;
            _tuningPanel.OnSimValueChanged = MarkTuningDirty;
            _tuningPanel.LoadFeelFromPrefs();

            _gearPanel = new GearPanelUI();
            _gearPanel.Game = _game;
            _gearPanel.Input = _input;
            _gearPanel.Load();

            _bindingsPanel = new BindingsPanelUI();
            _bindingsPanel.Bindings = _bindings;
            _bindingsPanel.Feel = _feel;

            _ui = new GameUI();
            _ui.Game = _game;
            _ui.Bindings = _bindings;
            _ui.Feel = _feel;
            _ui.Tuning = _tuningPanel;
            _ui.Controls = _bindingsPanel;
            _ui.Gear = _gearPanel;
            _ui.OnQuit = Quit;
            _ui.Initialise();
        }

        void MarkTuningDirty()
        {
            _tuningDirty = true;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.25f);

            _ui.Update(dt);
            _game.Update(dt);
            _sound.MasterVolume = _feel.masterVolume;

            if (_game.Client != null && _tuningPanel.ClientNet != _game.Client.NetTuning)
            {
                _tuningPanel.ClientNet = _game.Client.NetTuning;
                _tuningPanel.Rebuild();
            }

            // Tuning edits are batched: sliders fire every frame, the network does not need to.
            if (!_tuningDirty) return;
            _pushTuningTimer -= dt;
            if (_pushTuningTimer > 0f) return;
            _pushTuningTimer = 0.25f;
            _tuningDirty = false;
            if (_game.Server != null) _game.Server.PushTuning();
        }

        void OnGUI()
        {
            _ui.Draw();
        }

        void OnApplicationQuit()
        {
            Shutdown();
        }

        void OnDestroy()
        {
            if (Instance == this) Shutdown();
        }

        void Shutdown()
        {
            if (_tuningPanel != null) _tuningPanel.SaveFeelToPrefs();
            if (_bindings != null) _bindings.Save();
            if (_ui != null) _ui.Shutdown();
            if (_game != null) _game.Leave();
            if (_world != null) _world.Dispose();
            if (Instance == this) Instance = null;
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
