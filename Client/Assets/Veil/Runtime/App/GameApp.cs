using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Veil.Audio;
using Veil.Match;
using Veil.Net;
using Veil.Sim;
using Veil.UI;
using Veil.View;

namespace Veil.App
{
    /// <summary>
    /// Composition root and state machine: Title → Menu (Play / Characters / Leaderboard / Settings)
    /// → Match → Results → Play again. Owns the camera, world, UI canvas and network client.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public enum AppState { Title, Menu, Match, Results }

        public static GameApp I { get; private set; }

        public AppState State { get; private set; } = AppState.Title;
        public Profile Profile { get; private set; }
        public MapData Map { get; private set; }
        public WorldBuilder World { get; private set; }
        public CameraRig CamRig { get; private set; }
        public Camera Cam { get; private set; }
        public RectTransform Canvas { get; private set; }
        public VeilNetClient Net { get; private set; }
        public Stage Stage { get; private set; }
        public ProfileDto OnlineProfile;
        public string BackendUrl => BackendApi.BaseUrl(Profile.ServerHost, Profile.HttpPort);

        private TitleScreen _title;
        private MenuScreen _menu;
        private ResultsScreen _results;
        private PauseScreen _pause;
        private ClientMatch _match;
        private MatchView _matchView;
        private Hud _hud;
        private TouchControls _touch;
        private readonly InputCollector _input = new InputCollector();
        private float _endTimer = -1;
        private bool _paused;
        private string _shotDir;
        private bool _autotest, _scripted, _lineupShot, _lineupClose, _lineupBack;
        private string _onlineHost;
        private Light _sun;

        public ClientMatch CurrentMatch => _match;
        public bool IsOnlineMatch => _match != null && _match.Driver.IsOnline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("VEIL");
            go.AddComponent<GameApp>();
        }

        private void Awake()
        {
            I = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = Platform.IsMobile ? 60 : 120;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 1;
            Application.runInBackground = true;

            Profile = Profile.Load();
            MaterialLib.Init();
            UIKit.Init();
            Icons.Init();
            Sfx.Init();
            ApplySettings();

            SetupRendering();
            Map = ArenaMap.Build();
            World = new WorldBuilder(Map);
            World.BuildAll();
            Fx.Create(null);
            Stage = new Stage(Profile.Look);

            SetupCanvas();
            Net = new VeilNetClient();
            Net.MatchStarted += OnOnlineMatchStart;

            _title = new TitleScreen(Canvas, this);
            _menu = new MenuScreen(Canvas, this);
            _results = new ResultsScreen(Canvas, this);
            _pause = new PauseScreen(Canvas, this);
            GoTitle();

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autotest") _autotest = true;
                if (args[i] == "-autotest-scripted") { _autotest = true; _scripted = true; }
                if (args[i] == "-autotest-online" && i + 1 < args.Length) { _autotest = true; _scripted = true; _onlineHost = args[i + 1]; }
                if (args[i] == "-shotdir" && i + 1 < args.Length) _shotDir = args[i + 1];
            }
            if (_autotest) StartCoroutine(_onlineHost != null ? OnlineTest() : _scripted ? ScriptedTest() : AutoTest());
        }

        // ------------------------------------------------------------------ setup

        private void SetupRendering()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            DontDestroyOnLoad(Cam.gameObject);
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 900f;
            Cam.fieldOfView = 60f;
            Cam.clearFlags = CameraClearFlags.Skybox;
            var camData = Cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = Platform.IsMobile ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            CamRig = Cam.gameObject.AddComponent<CameraRig>();
            CamRig.Init(Cam);

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) _sun = l;
            if (_sun == null)
            {
                var lg = new GameObject("Sun");
                _sun = lg.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            DontDestroyOnLoad(_sun.gameObject);
            _sun.transform.rotation = Quaternion.Euler(48f, -38f, 0);
            _sun.color = new Color(1f, 0.95f, 0.86f);
            _sun.intensity = 1.35f;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.75f;

            RenderSettings.skybox = MaterialLib.Sky();
            MaterialLib.Sky().SetVector("_SunDir", -_sun.transform.forward);
            var sky = new Color(0.62f, 0.74f, 0.98f);
            var equator = new Color(0.7f, 0.74f, 0.82f);
            var ground = new Color(0.42f, 0.46f, 0.36f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(equator * 0.55f);
            sh.AddDirectionalLight(Vector3.up, sky * 0.55f, 1f);
            sh.AddDirectionalLight(Vector3.down, ground * 0.35f, 1f);
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.86f, 1f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 330f;

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = 70f;
                urp.shadowCascadeCount = 2;
                urp.renderScale = Profile.Quality == 0 ? 0.8f : 1f;
            }

            // post-processing: bloom for the glowing objectives, gentle grading for a bright cartoon look
            var volGo = new GameObject("PostFX");
            DontDestroyOnLoad(volGo);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.62f);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(14f);
            ca.contrast.Override(8f);
            ca.postExposure.Override(0.15f);
            var vg = profile.Add<Vignette>(true);
            vg.intensity.Override(0.2f);
            vg.smoothness.Override(0.5f);
            vol.sharedProfile = profile;
        }

        private void SetupCanvas()
        {
            var es = FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                es = esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
                DontDestroyOnLoad(esGo);
            }
            var cGo = new GameObject("UI");
            DontDestroyOnLoad(cGo);
            var canvas = cGo.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = cGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            cGo.AddComponent<GraphicRaycaster>();
            Canvas = (RectTransform)cGo.transform;
        }

        public void ApplySettings()
        {
            Sfx.Volume = Profile.SfxVolume;
            Sfx.SetMusic(Profile.Music);
            if (CamRig != null) CamRig.Sensitivity = Profile.Sensitivity;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                bool mob = Platform.IsMobile;
                urp.renderScale = mob ? (Profile.Quality == 0 ? 0.7f : 0.85f) : (Profile.Quality == 0 ? 0.8f : 1f);
                urp.shadowDistance = mob ? (Profile.Quality == 0 ? 20f : 35f) : (Profile.Quality == 0 ? 35f : 70f);
                if (mob) urp.shadowCascadeCount = 1;
                QualitySettings.lodBias = mob ? 0.6f : 1.5f;
            }
            var mode = Profile.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode && !Application.isEditor) Screen.fullScreenMode = mode;
        }

        // ------------------------------------------------------------------ states

        private void HideAll()
        {
            _title.Show(false);
            _menu.Show(false);
            _results.Show(false);
            _pause.Show(false);
        }

        public void GoTitle()
        {
            HideAll();
            State = AppState.Title;
            _title.Show(true);
            Stage.SetVisible(false);
        }

        public void GoMenu(int tab = 0)
        {
            HideAll();
            State = AppState.Menu;
            Stage.SetVisible(true);
            Stage.LobbyPose(Profile.Look);
            _menu.Show(true);
            _menu.SelectTab(tab);
        }

        public void StartOfflineMatch(bool autopilot = false)
        {
            var settings = new MatchSettings
            {
                MatchSeconds = Mathf.Clamp(Profile.MatchMinutes, 1, 15) * 60,
                TotalPlayers = Mathf.Clamp(Profile.Bots + 1, 2, GameConfig.MaxPlayers),
                Seed = UnityEngine.Random.Range(1, int.MaxValue),
            };
            var driver = new LocalMatchDriver(Map, settings, Profile.Name, Profile.Look, autopilot);
            BeginMatch(driver);
        }

        private void OnOnlineMatchStart(MatchStartMsg start)
        {
            if (_match != null) EndMatchCleanup();
            BeginMatch(new NetMatchDriver(Net, start));
        }

        private void BeginMatch(IMatchDriver driver)
        {
            HideAll();
            State = AppState.Match;
            Stage.SetVisible(false);
            _match = new ClientMatch(driver, Map, _input);
            _matchView = new MatchView(_match, World, CamRig, null);
            _hud = new Hud(Canvas, _match, _matchView, Cam, Platform.IsMobile);
            if (Platform.IsMobile && !driver.Autopilot) _touch = new TouchControls(Canvas, _match, () => SetPaused(!_paused));
            _hud.Root.SetAsFirstSibling();
            _match.MatchEnded += _ => _endTimer = 3f;
            _endTimer = -1;
            _paused = false;
            CamRig.Map = Map;
            CamRig.Pitch = 18f;
            CamRig.Distance = 7.5f;
            // face the arena centre from the spawn
            _match.Driver.Poll();
            if (_match.Latest != null)
            {
                var p = _match.Latest.Self.Pos;
                CamRig.Yaw = (-p).Yaw;
            }
            CamRig.SetFov(62f);
            Sfx.Play(Sfx.Start, 0.8f);
        }

        private void EndMatchCleanup()
        {
            _touch?.Dispose();
            _touch = null;
            _hud?.Dispose();
            _matchView?.Dispose();
            _match?.Dispose();
            _hud = null;
            _matchView = null;
            _match = null;
        }

        private void ShowResults(List<PlayerResult> results, int localId)
        {
            bool online = IsOnlineMatch;
            EndMatchCleanup();
            HideAll();
            State = AppState.Results;
            Stage.SetVisible(true);
            Stage.Podium(results);
            _results.Show(true);
            _results.Fill(results, localId, online);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void LeaveMatch()
        {
            if (_match == null) return;
            if (_match.Driver.IsOnline) Net.Disconnect();
            EndMatchCleanup();
            GoMenu(0);
        }

        public void SetPaused(bool p)
        {
            _paused = p;
            if (_match != null) _match.Paused = p;
            if (p) VirtualInput.Reset();          // don't keep firing/moving with a finger that was down
            _pause.Show(p);
            _pause.Root.SetAsLastSibling();       // above the touch controls, whose look pad would eat taps
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            float dt = Time.deltaTime;
            Net.Poll();
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            switch (State)
            {
                case AppState.Title:
                    _title.Update(dt);
                    var ts = Touchscreen.current;
                    if (!_autotest && ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                                       (ts != null && ts.primaryTouch.press.wasPressedThisFrame)))
                    {
                        Sfx.Play(Sfx.Click);
                        GoMenu(0);
                    }
                    break;
                case AppState.Menu:
                    _menu.Update(dt);
                    break;
                case AppState.Results:
                    _results.Update(dt);
                    break;
                case AppState.Match:
                    UpdateMatch(dt, kb, mouse);
                    break;
            }
            Stage.Update(dt);
        }

        private void UpdateMatch(float dt, Keyboard kb, Mouse mouse)
        {
            if (_match == null) return;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && _endTimer < 0) SetPaused(!_paused);
            if (kb != null && kb.f9Key.wasPressedThisFrame) _match.Driver.DebugSkip(60f);

            bool inputOn = !_paused && _endTimer < 0 && !_match.Driver.Autopilot && (Application.isFocused || Platform.IsMobile);
            bool lockCursor = inputOn && !Platform.IsMobile;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;
            if (_touch != null) { _touch.Update(); _hud?.SetScoreboard(_touch.ScoreboardOpen); }

            _match.Update(dt, CamRig.Yaw, ComputeAimYaw(), inputOn);
            _matchView?.Update(dt);
            _hud?.Update(dt, CamRig.Yaw);

            if (_endTimer >= 0)
            {
                _endTimer -= dt;
                if (_endTimer < 2.9f && _endTimer + dt >= 2.9f) _hud?.Banner("MATCH OVER", "Calculating scores…", 3f);
                if (_endTimer <= 0 && _match != null) ShowResults(_match.Results, _match.LocalId);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            switch (State)
            {
                case AppState.Title:
                    CamRig.Shot(new Vector3(Mathf.Sin(t * 0.05f) * 70f, 30f, Mathf.Cos(t * 0.05f) * 70f), new Vector3(0, 8, 0), dt, 1.5f);
                    break;
                case AppState.Menu:
                    if (_lineupBack) CamRig.Shot(Stage.Origin + new Vector3(0, 1.45f, 4.6f), Stage.Origin + new Vector3(0, 1.1f, 0), dt, 12f);
                    else if (_lineupShot) CamRig.Shot(Stage.Origin + (_lineupClose ? new Vector3(0, 1.7f, -2.4f) : new Vector3(0, 1.35f, -5.6f)), Stage.Origin + (_lineupClose ? new Vector3(0, 1.45f, 0) : new Vector3(0, 1.05f, 0)), dt, 12f);
                    else if (_menu.Tab == 1) CamRig.Shot(Stage.Origin + new Vector3(1.3f, 1.45f, -3.9f), Stage.Origin + new Vector3(1.3f, 1.1f, 0), dt, 4f);
                    else if (_menu.Tab == 3) CamRig.Shot(Stage.Origin + new Vector3(0, 2.4f, -8.5f), Stage.Origin + new Vector3(0, 1.6f, 0), dt, 3f);
                    else CamRig.Shot(Stage.Origin + new Vector3(1.9f, 2.0f, -7.2f), Stage.Origin + new Vector3(1.9f, 1.45f, 0), dt, 3f);
                    break;
                case AppState.Results:
                    CamRig.Shot(Stage.Origin + new Vector3(-2.4f, 2.3f, -7.6f), Stage.Origin + new Vector3(-2.4f, 1.6f, 0), dt, 3f);
                    break;
                case AppState.Match:
                    if (_match == null || _matchView == null) break;
                    Vector2 look = Vector2.zero;
                    float scroll = 0;
                    if (!_paused && Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
                    {
                        look = Mouse.current.delta.ReadValue();
                        scroll = Mathf.Clamp(Mouse.current.scroll.ReadValue().y / 120f, -2, 2);
                    }
                    if (_touch != null && !_paused)
                    {
                        look = VirtualInput.TakeLook();
                        // gyroscope aiming: turn/tilt the phone
                        int gmode = Profile.GyroMode;
                        bool gyroOn = gmode == (int)GyroAim.Mode.Always || (gmode == (int)GyroAim.Mode.WhileFiring && VirtualInput.FireHeld);
                        GyroAim.SetEnabled(gmode != (int)GyroAim.Mode.Off);
                        if (gyroOn && _match.Predicted.Alive)
                        {
                            var g = GyroAim.ReadDegrees(dt, Profile.GyroSensitivity, Profile.GyroInvertX, Profile.GyroInvertY);
                            CamRig.Yaw += g.x;
                            CamRig.Pitch = Mathf.Clamp(CamRig.Pitch - g.y, -8f, 65f);
                        }
                        if (VirtualInput.FireHeld && !gyroOn) AimAssist(dt);
                    }
                    if (_match.Driver.Autopilot || _match.ScriptedInput != null)
                    {
                        // cinematic follow for autopilot/spectating
                        CamRig.Yaw = Mathf.LerpAngle(CamRig.Yaw, _match.Predicted.Yaw, 1 - Mathf.Exp(-2f * dt));
                    }
                    var me = _match.Predicted;
                    float fov = 62f + (me.Sprinting ? 5f : 0f) + (me.DashT > 0 ? 10f : 0f);
                    CamRig.SetFov(fov);
                    Vector3 target = _matchView.Local.Pos;
                    if (!_match.Predicted.Alive) target += Vector3.up * 2f;
                    CamRig.Follow(target, look, scroll, dt);
                    _hud?.PlaceCrosshair(dt);   // after the camera moved, so it tracks without lag
                    break;
            }
        }

        /// <summary>
        /// Third-person aim convergence: cast a ray from the camera through the crosshair, find the first
        /// thing it hits (enemy, wall, ground or max range) and fire from the player toward that point.
        /// Without this the offset/elevated camera makes shots land left of and below the crosshair.
        /// </summary>
        private float ComputeAimYaw()
        {
            if (_match == null || _matchView == null || Cam == null) return CamRig.Yaw;
            var ray = Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Vector3 player = _matchView.Local.Pos;
            // ignore everything between the camera and the player
            float tMin = Mathf.Max(0.5f, Vector3.Dot(player + Vector3.up * 1.2f - ray.origin, ray.direction));
            float tMax = GameConfig.ProjectileRange + tMin + 5f;
            float best = tMax;

            // enemies / decoys: vertical capsule approximation
            Vector2 o2 = new Vector2(ray.origin.x, ray.origin.z), d2 = new Vector2(ray.direction.x, ray.direction.z);
            float dd = Mathf.Max(1e-5f, Vector2.Dot(d2, d2));
            foreach (var kv in _matchView.Avatars)
            {
                var av = kv.Value;
                if (av.IsLocal || !av.Alive || av.Fade < 0.3f) continue;
                Vector2 p2 = new Vector2(av.Pos.x, av.Pos.z);
                float t = Vector2.Dot(p2 - o2, d2) / dd;
                if (t < tMin || t > best) continue;
                float y = ray.origin.y + ray.direction.y * t;
                if (y < av.Pos.y - 0.1f || y > av.Pos.y + 2.3f) continue;
                if ((o2 + d2 * t - p2).magnitude < 0.65f) best = t;
            }
            // ground
            if (ray.direction.y < -0.001f)
            {
                float tg = -ray.origin.y / ray.direction.y;
                if (tg > tMin && tg < best) best = tg;
            }
            // walls / props (march)
            for (float t = tMin; t < best; t += 0.35f)
            {
                var p = ray.GetPoint(t);
                if (Map.BlocksShotAt(new Vec2(p.x, p.z), Mathf.Max(0f, p.y), 0.05f)) { best = t; break; }
            }
            Vector3 aim = ray.GetPoint(best);
            Vector2 dir = new Vector2(aim.x - player.x, aim.z - player.z);
            if (dir.sqrMagnitude < 1.5f * 1.5f) return CamRig.Yaw;
            return Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        }

        /// <summary>Mobile: while firing, gently pull the camera toward the nearest visible enemy in front.</summary>
        private void AimAssist(float dt)
        {
            var snap = _match.Latest;
            if (snap == null) return;
            var me = _match.Predicted;
            float best = 999, bestYaw = CamRig.Yaw;
            foreach (var a in snap.Avatars)
            {
                if (a.Vis != Visibility.Full || (a.Flags & AvatarFlags.MyDecoy) != 0) continue;
                var d = a.Pos - me.Pos;
                float dist = d.Length;
                if (dist > 24f || dist < 0.5f) continue;
                float yaw = d.Yaw;
                float off = Mathf.Abs(Mathf.DeltaAngle(CamRig.Yaw, yaw));
                if (off > 32f) continue;
                float score = off + dist * 0.8f;
                if (score < best) { best = score; bestYaw = yaw; }
            }
            if (best < 999) CamRig.Yaw = Mathf.MoveTowardsAngle(CamRig.Yaw, bestYaw, 110f * dt);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && State == AppState.Match && _match != null && !_match.Driver.IsOnline && !_paused) SetPaused(true);
        }

        private void OnApplicationQuit()
        {
            if (Net != null && Net.Status == VeilNetClient.State.Connected) Net.Disconnect();
        }

        // ------------------------------------------------------------------ autotest (screenshots for CI / review)

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(_shotDir)) yield break;
            Directory.CreateDirectory(_shotDir);
            ScreenCapture.CaptureScreenshot(Path.Combine(_shotDir, name + ".png"));
            Debug.Log("[VEIL] screenshot " + name);
            yield return null;
        }

        private void LogPrediction()
        {
            if (_match == null) return;
            Debug.Log($"[VEIL] prediction: {_match.Corrections} snapshots, avg correction {(_match.Corrections > 0 ? _match.CorrectionSum / _match.Corrections : 0):0.0000} m, max {_match.CorrectionMax:0.000} m");
        }

        /// <summary>Offline match driven through the real human input/prediction path.</summary>
        private IEnumerator ScriptedTest()
        {
            yield return new WaitForSeconds(2f);
            Profile.MatchMinutes = 1;
            StartOfflineMatch(false);
            _match.ScriptedInput = new ScriptedPilot(_match).Next;
            yield return new WaitForSeconds(8f);
            yield return Shot("s1_scripted");
            for (int b = 0; b < 3; b++) { yield return new WaitForSeconds(0.12f); yield return Shot("s1_run" + b); }
            yield return new WaitForSeconds(8f);
            yield return Shot("s2_scripted");
            LogPrediction();
            bool logged = false;
            while (State == AppState.Match) { if (!logged && _match != null && _match.Ended) { LogPrediction(); logged = true; } yield return null; }
            yield return new WaitForSeconds(2f);
            yield return Shot("s3_results");
            Application.Quit();
        }

        /// <summary>Connect to a server, ready up, play one match with scripted input, quit.</summary>
        private IEnumerator OnlineTest()
        {
            yield return new WaitForSeconds(2f);
            Profile.ServerHost = _onlineHost;
            GoMenu(0);
            yield return BackendApi.Register(BackendUrl, "AutoTester", r =>
            {
                Profile.BackendId = r.id; Profile.BackendToken = r.token; OnlineProfile = r.profile;
                Debug.Log($"[VEIL] online test: registered backend profile {r.id} (level {r.profile.level}, rating {r.profile.rating})");
            }, e => Debug.Log("[VEIL] online test: backend register failed " + e));
            Net.Connect(_onlineHost, Profile.ServerPort, new HelloMsg { Name = "AutoTester", Look = Profile.Look, ProfileId = Profile.BackendId ?? "" });
            float t = 0;
            while (Net.Status != VeilNetClient.State.Connected && t < 10) { t += Time.deltaTime; yield return null; }
            Debug.Log($"[VEIL] online test: net status {Net.Status} {Net.LastError}");
            if (Net.Status != VeilNetClient.State.Connected) { Application.Quit(); yield break; }
            while (Net.Lobby == null) yield return null;
            Net.SendReady(true, 60);
            Debug.Log("[VEIL] online test: ready");
            while (State != AppState.Match) yield return null;
            _match.ScriptedInput = new ScriptedPilot(_match).Next;
            Debug.Log("[VEIL] online test: match started");
            yield return new WaitForSeconds(10f);
            yield return Shot("o1_online");
            LogPrediction();
            yield return new WaitForSeconds(10f);
            yield return Shot("o2_online");
            LogPrediction();
            bool logged = false;
            while (State == AppState.Match) { if (!logged && _match != null && _match.Ended) { LogPrediction(); logged = true; } yield return null; }
            yield return new WaitForSeconds(2.5f);
            yield return Shot("o3_results");
            yield return BackendApi.GetProfile(BackendUrl, Profile.BackendId, p => Debug.Log($"[VEIL] online test: after match level {p.level} xp {p.xp} rating {p.rating} matches {p.matches} best {p.bestScore}"), e => Debug.Log("[VEIL] profile fetch failed " + e));
            Debug.Log("[VEIL] online test done");
            Net.Disconnect();
            Application.Quit();
        }

        private IEnumerator AutoTest()
        {
            Debug.Log("[VEIL] autotest start");
            yield return new WaitForSeconds(3f);
            yield return Shot("01_title");
            GoMenu(0);
            yield return new WaitForSeconds(2.5f);
            yield return Shot("02_lobby");
            // clean character lineup (no UI) to compare with the concept art
            Canvas.gameObject.SetActive(false);
            _lineupShot = true;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("02b_lineup");
            _lineupClose = true;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("02c_closeup");
            _lineupBack = true;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("02d_lineup_back");
            _lineupBack = false;
            _lineupShot = _lineupClose = false;
            Canvas.gameObject.SetActive(true);
            _menu.SelectTab(1);
            yield return new WaitForSeconds(2.5f);
            yield return Shot("03_characters");
            _menu.SelectTab(3);
            yield return new WaitForSeconds(1f);
            yield return Shot("04_settings");

            Profile.MatchMinutes = 5;
            StartOfflineMatch(true);
            yield return new WaitForSeconds(6f);
            yield return Shot("05_match_start");
            yield return new WaitForSeconds(12f);
            yield return Shot("06_match_action");
            _match.Driver.DebugSkip(70f);
            yield return new WaitForSeconds(10f);
            yield return Shot("07_competition");
            CamRig.Pitch = 55f; CamRig.Distance = 14f;
            yield return new WaitForSeconds(2f);
            yield return Shot("08_overview");
            CamRig.Pitch = 18f; CamRig.Distance = 7.5f;
            _match.Driver.DebugSkip(150f);
            yield return new WaitForSeconds(8f);
            yield return Shot("09_collapse");
            _match.Driver.DebugSkip(100f);
            while (State == AppState.Match) yield return null;
            yield return new WaitForSeconds(3f);
            yield return Shot("10_results");
            Debug.Log("[VEIL] autotest done");
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }
    }

    /// <summary>3D stage in front of the Tower used by the menu (lineup) and the results (podium).</summary>
    public sealed class Stage
    {
        public readonly Vector3 Origin = new Vector3(0, 0, -9.5f);
        private readonly Transform _root;
        private readonly CharacterRig[] _rigs = new CharacterRig[5];
        private readonly Transform[] _pedestals = new Transform[3];
        private bool _podium;
        private float _t;

        public Stage(Appearance mine)
        {
            _root = new GameObject("Stage").transform;
            _root.position = Origin;
            _root.rotation = Quaternion.Euler(0, 180, 0);
            for (int i = 0; i < 5; i++)
            {
                var look = i == 0 ? mine : Appearance.Preset(i);
                _rigs[i] = CharacterRig.Create(_root, look, true, "StageRig" + i);
            }
            var gold = MaterialLib.Toon(Palette.Gold, 0.4f, 0.8f);
            var silver = MaterialLib.Toon(Palette.Hex("#d8dcef"), 0.4f, 0.8f);
            var bronze = MaterialLib.Toon(Palette.Hex("#e0955a"), 0.4f, 0.8f);
            Material[] mats = { gold, silver, bronze };
            for (int i = 0; i < 3; i++)
            {
                var p = Build.Part(_root, MeshGen.Cylinder(20), mats[i], Vector3.zero, new Vector3(1.6f, 1, 1.6f), null, "Pedestal" + i);
                _pedestals[i] = p.transform;
                p.SetActive(false);
            }
        }

        public void SetVisible(bool v) => _root.gameObject.SetActive(v);

        /// <summary>World point above a stage character's head (for UI labels).</summary>
        public Vector3 HeadPoint(int i) => _rigs[i].transform.position + Vector3.up * 2.6f;

        private static readonly float[] LineX = { 0, -1.35f, 1.35f, -2.6f, 2.6f };

        public void LobbyPose(Appearance mine)
        {
            _podium = false;
            if (!_rigs[0].Look.Equals(mine)) _rigs[0].Rebuild(mine);
            for (int i = 0; i < 5; i++)
            {
                _rigs[i].gameObject.SetActive(true);
                _rigs[i].transform.localPosition = new Vector3(LineX[i], 0, i == 0 ? 0.2f : -0.5f - Mathf.Abs(LineX[i]) * 0.2f);
                _rigs[i].transform.localRotation = Quaternion.Euler(0, -LineX[i] * 5, 0);
                _rigs[i].ResetPose();
            }
            foreach (var p in _pedestals) p.gameObject.SetActive(false);
        }

        public void UpdateLook(Appearance mine)
        {
            _rigs[0].Rebuild(mine);
        }

        public void Podium(List<PlayerResult> results)
        {
            _podium = true;
            float[] x = { 0, 1.8f, -1.8f }; // stage faces the camera: local +X is screen-left
            float[] h = { 0.9f, 0.55f, 0.3f };
            for (int i = 0; i < 5; i++) _rigs[i].gameObject.SetActive(i < 3 && i < results.Count);
            for (int i = 0; i < 3 && i < results.Count; i++)
            {
                _rigs[i].Rebuild(results[i].Look);
                _rigs[i].transform.localPosition = new Vector3(x[i], h[i], 0);
                _rigs[i].transform.localRotation = Quaternion.identity;
                _rigs[i].ResetPose();
                _pedestals[i].gameObject.SetActive(true);
                _pedestals[i].localPosition = new Vector3(x[i], h[i] / 2, 0);
                _pedestals[i].localScale = new Vector3(1.6f, h[i], 1.6f);
            }
        }

        public void Update(float dt)
        {
            if (!_root.gameObject.activeSelf) return;
            _t += dt;
            for (int i = 0; i < 5; i++)
            {
                if (!_rigs[i].gameObject.activeSelf) continue;
                _rigs[i].Animate(new RigState { Grounded = true, Idle = true, Victory = _podium && i == 0, Aiming = !_podium && i == 2 && Mathf.Repeat(_t, 6f) < 2f }, dt);
            }
            if (GameApp.I != null && GameApp.I.State == GameApp.AppState.Menu && Mouse.current != null && Mouse.current.rightButton.isPressed)
                _rigs[0].transform.Rotate(0, -Mouse.current.delta.ReadValue().x * 0.4f, 0);
        }
    }
}
