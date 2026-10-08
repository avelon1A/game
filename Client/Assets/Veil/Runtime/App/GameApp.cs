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
using Veil.Voice;

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
        /// <summary>Realtime social connection: presence, friends, party, invites, matchmaking.</summary>
        public GatewayClient Gateway { get; private set; }
        /// <summary>Squad / party voice chat.</summary>
        public VoiceChat Voice { get; private set; }
        public Stage Stage { get; private set; }
        public ProfileDto OnlineProfile;
        public string BackendUrl => BackendApi.BaseUrl(Profile.ServerHost, Profile.HttpPort);

        private TitleScreen _title;
        private MenuScreen _menu;
        private ResultsScreen _results;
        private PauseScreen _pause;
        private Toasts _toasts;
        private string _sentLook = "";
        private ClientMatch _match;
        private MatchView _matchView;
        private Hud _hud;
        private TouchControls _touch;
        private readonly InputCollector _input = new InputCollector();
        private float _endTimer = -1;
        private bool _paused;
        public static bool Scoped { get; private set; }
        private string _shotDir;
        private bool _autotest, _scripted, _lineupShot, _lineupClose, _lineupBack;
        private string _onlineHost;
        private Light _sun;

        public ClientMatch CurrentMatch => _match;
        public int MenuTab => _menu != null ? _menu.Tab : 0;
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
            Net.Rejected += reason => _toasts?.Notice($"<color=#ff9a8a>{reason}</color>");
            Net.ServerFound += OnServerFound;
            Gateway = new GatewayClient();
            Gateway.MatchAssigned += OnMatchAssigned;
            Gateway.CredentialsIssued += (id, token) => { Profile.BackendId = id; Profile.BackendToken = token; Profile.Save(); };
            Gateway.Changed += () =>
            {
                // the server is the source of truth for the linked Google account
                if (Gateway.Online && Profile.GoogleEmail != Gateway.Email) { Profile.GoogleEmail = Gateway.Email; Profile.Save(); AccountChanged?.Invoke(); }
                // UDP mode has no REST: load the profile over the Gateway once connected
                if (Gateway.Online && Gateway.UsesUdp && !_profileRequested) { _profileRequested = true; FetchProfile(p => OnlineProfile = p, e => _profileRequested = false); }
                if (!Gateway.Online) _profileRequested = false;
            };
            Voice = new VoiceChat { Mode = (VoiceMode)Mathf.Clamp(Profile.VoiceMode, 0, 2), OutputVolume = Profile.VoiceVolume, Sensitivity = Profile.MicSensitivity };

            _title = new TitleScreen(Canvas, this);
            _menu = new MenuScreen(Canvas, this);
            _results = new ResultsScreen(Canvas, this);
            _pause = new PauseScreen(Canvas, this);
            _toasts = new Toasts(Canvas, this);
            GoTitle();
            // remote boot config: the cached copy already chose the server in Profile.Load; fetch the fresh one
            ApplyBoot(BootConfig.Current, cached: true);
            StartCoroutine(BootConfig.Fetch(d => ApplyBoot(d, cached: false)));

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autotest") _autotest = true;
                if (args[i] == "-autotest-scripted") { _autotest = true; _scripted = true; }
                if (args[i] == "-autotest-online" && i + 1 < args.Length) { _autotest = true; _scripted = true; _onlineHost = args[i + 1]; }
                if (args[i] == "-shotdir" && i + 1 < args.Length) _shotDir = args[i + 1];
                if (args[i] == "-walkpreview") { _autotest = true; _walkTest = true; }
                if (args[i] == "-mapshot") { StartCoroutine(MapShots()); return; }
                if (args[i] == "-sniper") { var l = Profile.Look; l.Weapon = 1; Profile.Look = l; Stage.UpdateLook(l); }   // test: equip the sniper
            }
            if (_autotest) StartCoroutine(_walkTest ? WalkPreviewTest() : _onlineHost != null ? OnlineTest() : _scripted ? ScriptedTest() : AutoTest());
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
            Sfx.SetMusicOn(Profile.MusicOn);
            if (CamRig != null) CamRig.Sensitivity = Profile.Sensitivity;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                bool mob = Platform.IsMobile;
                urp.renderScale = mob ? (Profile.Quality == 0 ? 0.7f : 0.85f) : (Profile.Quality == 0 ? 0.8f : 1f);
                urp.shadowDistance = mob ? (Profile.Quality == 0 ? 20f : 35f) : (Profile.Quality == 0 ? 35f : 70f);
                if (mob) urp.shadowCascadeCount = 1;
                QualitySettings.lodBias = mob ? 0.6f : 1.5f;
            }
            // phones are always fullscreen (immersive: no navigation / status bar); the setting is for desktop only
            var mode = Application.isMobilePlatform || Profile.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
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
                TotalPlayers = GameConfig.MaxPlayers,   // your squad (you + 3 bots) vs 3 bot squads
                Seed = UnityEngine.Random.Range(1, int.MaxValue),
            };
            var driver = new LocalMatchDriver(Map, settings, Profile.Name, Profile.Look, autopilot);
            BeginMatch(driver);
        }

        // ------------------------------------------------------------------ online (Gateway → party → match)

        private bool _goingOnline, _discovering, _profileRequested;

        /// <summary>Profile over REST, or over the Gateway when the server is reached through UDP only.</summary>
        public void FetchProfile(Action<ProfileDto> ok, Action<string> fail)
        {
            if (Profile.ServerUdp)
            {
                Gateway.RequestData(Gw.ProfileGet, null, (o, e, d) => { if (o && !string.IsNullOrEmpty(d)) ok(JsonUtility.FromJson<ProfileDto>(d)); else fail(e); });
                return;
            }
            if (string.IsNullOrEmpty(Profile.BackendId)) { fail("no profile"); return; }
            StartCoroutine(BackendApi.GetProfile(BackendUrl, Profile.BackendId, ok, fail));
        }

        public void FetchLeaderboard(Action<LeaderboardDto> ok, Action<string> fail)
        {
            if (Profile.ServerUdp)
            {
                Gateway.RequestData(Gw.LeaderboardGet, null, (o, e, d) => { if (o && !string.IsNullOrEmpty(d)) ok(JsonUtility.FromJson<LeaderboardDto>(d)); else fail(e); });
                return;
            }
            StartCoroutine(BackendApi.Leaderboard(BackendUrl, ok, fail));
        }

        /// <summary>Makes sure we have a backend profile, then opens the Gateway. Phones find the server on the LAN.</summary>
        public void GoOnline()
        {
            if (_goingOnline || Gateway.Online) return;
            if (BootConfig.UpdateRequired) { _menu?.Squad?.Flash("<color=#ff9a8a>Update Rilo to play online</color> (Settings → UPDATE)", 8); return; }
            if (BootConfig.Maintenance) { _menu?.Squad?.Flash($"<color=#ffd84a>{MaintenanceText}</color>", 8); return; }
            StartCoroutine(GoOnlineRoutine());
        }

        private static bool IsLoopback(string h) => string.IsNullOrWhiteSpace(h) || h == "127.0.0.1" || h == "localhost" || h == "::1";

        private IEnumerator GoOnlineRoutine()
        {
            _goingOnline = true;
            if (Profile.ServerUdp)
            {
                // internet mode through UDP-only tunnels: no REST, the Gateway hello creates / resumes the guest account
                _sentLook = Profile.AppearanceString;
                Gateway.Connect(Profile.ServerHost, Profile.HttpPort, Profile.BackendId ?? "", Profile.BackendToken ?? "", Profile.Name, _sentLook, udp: true);
                _goingOnline = false;
                yield break;
            }
            if (Platform.IsMobile && IsLoopback(Profile.ServerHost))
            {
                _menu?.Squad?.Flash("Looking for a VEIL server on your Wi-Fi…", 5);
                _discovering = true;
                for (int i = 0; i < 6 && IsLoopback(Profile.ServerHost); i++)
                {
                    Net.Discover(Profile.ServerPort);
                    float t = 0;
                    while (t < 0.5f && IsLoopback(Profile.ServerHost)) { t += Time.unscaledDeltaTime; yield return null; }
                }
                _discovering = false;
                if (IsLoopback(Profile.ServerHost))
                {
                    _menu?.Squad?.Flash("<color=#ff9a8a>No server found on this Wi-Fi — type the server PC's address</color>", 8);
                    _goingOnline = false;
                    yield break;
                }
            }
            string url = BackendUrl;
            string error = null;
            if (!string.IsNullOrEmpty(Profile.BackendId))
            {
                ProfileDto got = null;
                yield return BackendApi.GetProfile(url, Profile.BackendId, p => got = p, e => { });
                if (got == null) Profile.BackendId = "";
                else OnlineProfile = got;
            }
            if (string.IsNullOrEmpty(Profile.BackendId))
            {
                yield return BackendApi.Register(url, Profile.Name, r =>
                {
                    Profile.BackendId = r.id; Profile.BackendToken = r.token; OnlineProfile = r.profile; Profile.Save();
                }, e => error = e);
            }
            if (error != null || string.IsNullOrEmpty(Profile.BackendId))
            {
                _menu?.Squad?.Flash($"<color=#ff9a8a>Server not reachable at {Profile.ServerHost} ({error})</color>", 8);
                _goingOnline = false;
                yield break;
            }
            _sentLook = Profile.AppearanceString;
            Gateway.Connect(Profile.ServerHost, Profile.HttpPort, Profile.BackendId, Profile.BackendToken, Profile.Name, _sentLook);
            if (OnlineProfile == null) FetchProfile(p => OnlineProfile = p, e => { });
            _goingOnline = false;
        }

        private string _bootNoticeShown = "";
        private static string MaintenanceText => string.IsNullOrEmpty(BootConfig.Current?.message) ? "Online play is paused for maintenance — back soon" : BootConfig.Current.message;

        /// <summary>Applies the boot config: server switch (live reconnect), maintenance, update prompts, announcement.</summary>
        private void ApplyBoot(BootData d, bool cached)
        {
            if (d == null) return;
            if (!cached && Profile.SetAutoServer(d.server))
            {
                Debug.Log($"[VEIL] boot config moved the server to {Profile.ServerAddress}");
                _menu?.Squad?.SetHostText(Profile.ServerAddress);
                if (State != AppState.Match && Gateway.Status != GatewayClient.State.Offline) { Gateway.Disconnect(); _profileRequested = false; }
                if (State != AppState.Match && (_menu?.Squad?.Online ?? false)) GoOnline();
            }
            if (BootConfig.Maintenance && Gateway.Online && State != AppState.Match) Gateway.Disconnect();
            string note = BootConfig.UpdateRequired ? "A new version of Rilo is needed to play online — Settings → UPDATE"
                : BootConfig.Maintenance ? MaintenanceText
                : !string.IsNullOrEmpty(d.message) ? d.message
                : BootConfig.UpdateAvailable ? "A new version of Rilo is available — Settings → UPDATE" : "";
            if (!string.IsNullOrEmpty(note) && note != _bootNoticeShown) { _bootNoticeShown = note; _toasts?.Notice(note, 7f); }
        }

        // ------------------------------------------------------------------ Google sign-in (Android)

        /// <summary>Raised when the player signs in / out (Settings refreshes its account row).</summary>
        public event Action AccountChanged;
        public void Toast(string text) => _toasts?.Notice(text);
        /// <summary>Coins (unlock heroes / guns). Server-side economy comes next; until then the cached value.</summary>
        public int Coins => OnlineProfile != null ? OnlineProfile.coins : PlayerPrefs.GetInt("coins", 0);
        public int Gems => 0;
        public bool SignedInWithGoogle => !string.IsNullOrEmpty(Profile.GoogleEmail);
        private string GoogleClientId
        {
            get
            {
                var id = BootConfig.Current?.googleClientId;
                if (!string.IsNullOrEmpty(id)) return id.Trim();
                var res = Resources.Load<TextAsset>("google_client_id");
                return res != null ? res.text.Trim() : "";
            }
        }

        /// <summary>Links this player to a Google account (keeps guest progress) or switches to the account that owns it.</summary>
        public void SignInWithGoogle(Action<bool> finished = null)
        {
            if (!Gateway.Online) { _toasts?.Notice("Connecting to the server — try again in a moment"); GoOnline(); finished?.Invoke(false); return; }
            GoogleSignIn.SignIn(GoogleClientId, r =>
            {
                if (!r.Ok) { if (!r.Cancelled) _toasts?.Notice($"<color=#ff9a8a>Google sign-in failed</color> ({r.Error})", 6f); finished?.Invoke(false); return; }
                Gateway.RequestData(Gw.AuthGoogle, new GwGoogleAuth { idToken = r.IdToken }, (ok, err, data) =>
                {
                    if (!ok) { _toasts?.Notice($"<color=#ff9a8a>{err}</color>", 6f); finished?.Invoke(false); return; }
                    var a = JsonUtility.FromJson<GwAuthResult>(data);
                    Profile.GoogleEmail = a.email;
                    if (a.switched)
                    {
                        // this Google account already has a player (e.g. from another phone): use it
                        Profile.BackendId = a.id; Profile.BackendToken = a.token; Profile.Save();
                        Reconnect();
                        _toasts?.Notice($"Signed in as {a.email} — welcome back, {a.handle}", 6f);
                    }
                    else
                    {
                        Profile.Save();
                        _toasts?.Notice($"Signed in with Google ({a.email}) — your progress is saved", 6f);
                    }
                    AccountChanged?.Invoke();
                    finished?.Invoke(true);
                });
            });
        }

        /// <summary>Signs out: forgets the Google account on this device and continues as a new guest.</summary>
        public void SignOutGoogle()
        {
            GoogleSignIn.SignOut();
            Profile.GoogleEmail = ""; Profile.BackendId = ""; Profile.BackendToken = ""; Profile.Save();
            Reconnect();
            _toasts?.Notice("Signed out — playing as a new guest. Sign in again to get your account back.", 6f);
            AccountChanged?.Invoke();
        }

        private void Reconnect()
        {
            Gateway.Disconnect();
            OnlineProfile = null;
            _profileRequested = false;
            GoOnline();
        }

        /// <summary>Opens the update link from the boot config (APK / store page).</summary>
        public void OpenUpdate()
        {
            var url = BootConfig.Current?.updateUrl;
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
            else _toasts?.Notice(BootConfig.UpdateAvailable || BootConfig.UpdateRequired ? "Ask for the new Rilo APK" : $"You have the latest Rilo (build {BootConfig.Build})", 4f);
        }

        private void OnServerFound(string ip, int port, string name, int players)
        {
            if (!_discovering) return;
            Profile.ServerHost = ip;
            Profile.Save();
            _menu?.Squad?.SetHostText(ip);
            _menu?.Squad?.Flash($"Found <color=#b9f27c>{name}</color> on your Wi-Fi ({ip})", 4);
        }

        private void OnMatchAssigned(MatchAssignedMsg a)
        {
            if (a.rejoin)
            {
                _toasts?.Notice("Your squad's match is still running — tap <color=#ffd84a>REJOIN</color>");
                if (State == AppState.Menu) ShowSquad();
                return;
            }
            JoinAssignedMatch(a);
        }

        /// <summary>Connects to the match host with the signed ticket (also used to REJOIN after leaving / a crash).</summary>
        public void JoinAssignedMatch(MatchAssignedMsg a)
        {
            if (a == null) return;
            string host = string.IsNullOrEmpty(a.host) ? Gateway.Host : a.host;
            if (Net.Status == VeilNetClient.State.Connected || Net.Status == VeilNetClient.State.Connecting) Net.Disconnect();
            Net.Connect(host, a.port, new HelloMsg { Name = Profile.Name, Look = Profile.Look, ProfileId = Profile.BackendId ?? "", Ticket = a.ticket });
            _toasts?.Notice($"Joining match · Squad {(char)('A' + a.squad)}");
        }

        /// <summary>Menu → PLAY tab in online squad mode.</summary>
        public void ShowSquad()
        {
            if (State != AppState.Menu) return;
            _menu.SelectTab(0);
            _menu.Squad.SetMode(true);
        }

        private void UpdateSocial(float dt)
        {
            Gateway.Update(dt);
            if (Gateway.Online && Profile.AppearanceString != _sentLook) { _sentLook = Profile.AppearanceString; Gateway.SetLook(_sentLook); }

            // voice channel: squad channel during an online match, party channel in the lobby
            string ch = null, tok = null;
            var a = Gateway.Assignment;
            if (State == AppState.Match && IsOnlineMatch && a != null) { ch = a.voiceChannel; tok = a.voiceToken; }
            else if (State != AppState.Match && Gateway.Online && !Gateway.Party.Empty && Gateway.Party.members.Count > 1) { ch = Gateway.Party.voiceChannel; tok = Gateway.Party.voiceToken; }
            if (ch != null && Voice.Mode != VoiceMode.Off) Voice.JoinChannel(Gateway.VoiceHost, Gateway.VoicePort, ch, tok, Gateway.MyId);
            else if (!string.IsNullOrEmpty(Voice.Channel)) Voice.LeaveChannel();
            var kb = Keyboard.current;
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            Voice.PushToTalkHeld = (!typing && kb != null && kb.vKey.isPressed) || VirtualInput.TalkHeld;
            Voice.Update(dt);
            _toasts?.Update(dt);
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
            CamRig.Distance = Profile.CamDistance;   // Settings / pause: CAMERA DISTANCE (phones default close)
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
            ResultsScreen.Winner = _match?.Latest?.Winner ?? -1;
            ResultsScreen.SquadStages = _match?.Latest != null ? (byte[])_match.Latest.SquadStage.Clone() : null;
            EndMatchCleanup();
            HideAll();
            State = AppState.Results;
            Stage.SetVisible(true);
            Stage.Podium(ResultsScreen.PodiumSquad(results));
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

        private float _fpsT; private int _fpsN; private float _fpsWorst;

        private void Update()
        {
            float dt = Time.deltaTime;
            // performance log (logcat / player log): average fps + worst frame every 5 s while in a match
            if (State == AppState.Match)
            {
                _fpsT += Time.unscaledDeltaTime; _fpsN++; _fpsWorst = Mathf.Max(_fpsWorst, Time.unscaledDeltaTime);
                if (_fpsT >= 5f) { Debug.Log($"[VEIL] perf {_fpsN / _fpsT:0} fps, worst {_fpsWorst * 1000:0} ms"); _fpsT = 0; _fpsN = 0; _fpsWorst = 0; }
            }
            Net.Poll();
            GoogleSignIn.Pump();
            UpdateSocial(Time.unscaledDeltaTime);
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            switch (State)
            {
                case AppState.Title:
                    _title.Update(dt);
                    var ts = Touchscreen.current;
                    if (!_autotest && !_title.Choosing && ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
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
            bool lockCursor = inputOn && !Platform.IsMobile && !(_hud != null && _hud.PuzzleOpen);   // free the mouse for the circuit puzzle
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
            UpdateBackdrop();
            if (!(State == AppState.Menu && (Stage.SquadMode || Stage.SoloMode))) _lobbyShot = 0;
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
                    else if (Stage.SquadMode || Stage.SoloMode)
                    {
                        // painted lobby: squad left of the squad panel / your hero left of the Characters & Leaderboard panels
                        float x = Stage.SquadMode ? 0f : 1.75f;   // squad: centred between the side menu and the right cards
                        int key = Stage.SquadMode ? 1 : 2;
                        // slow "breathing" drift so the 3D heroes and the motes move against the painted scene
                        var breath = new Vector3(Mathf.Sin(t * 0.21f) * 0.12f, Mathf.Sin(t * 0.33f) * 0.04f, Mathf.Sin(t * 0.17f) * 0.1f);
                        // squad: low hero shot (camera just under chest height, looking slightly up) like the concept art
                        if (Stage.SquadMode) CamRig.Shot(Stage.Origin + new Vector3(0, 0.95f, -3.15f) + breath, Stage.Origin + new Vector3(0, 1.1f, 0), dt, _lobbyShot == key ? 3f : 10000f);
                        else CamRig.Shot(Stage.Origin + new Vector3(x, 1.3f, -3.9f) + breath, Stage.Origin + new Vector3(x, 1.4f, 0), dt, _lobbyShot == key ? 3f : 10000f);
                        _lobbyShot = key;
                    }
                    else CamRig.Shot(Stage.Origin + new Vector3(1.9f, 2.0f, -7.2f), Stage.Origin + new Vector3(1.9f, 1.45f, 0), dt, 3f);
                    break;
                case AppState.Results:
                    // podium on the right of the screen, the results card on the left (stage local +X is screen-left)
                    // MVP close-up on the right of the screen, the results card on the left
                    CamRig.Shot(Stage.Origin + new Vector3(-0.62f, 1.62f, -1.75f) + new Vector3(Mathf.Sin(t * 0.21f) * 0.03f, Mathf.Sin(t * 0.33f) * 0.01f, 0), Stage.Origin + new Vector3(-0.62f, 1.52f, 0), dt, 3f);   // waist-up
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
                    float fov = (Platform.IsMobile ? 50f : 62f) + (me.Sprinting ? 5f : 0f) + (me.DashT > 0 ? 10f : 0f);   // wide phone screens: narrower FOV so the world looks closer
                    // sniper scope: hold right mouse (desktop) or the SCOPE toggle (phones)
                    Scoped = me.Alive && me.Look.Weapon == 1 && !(_match.Latest?.Self?.Fists ?? false) && (VirtualInput.ScopeOn || (Mouse.current != null && Mouse.current.rightButton.isPressed && !Platform.IsMobile));
                    if (Scoped) fov = GameConfig.ScopeFov;
                    CamRig.SetFov(fov);
                    Vector3 target = _matchView.Local.Pos;
                    if (!_match.Predicted.Alive)
                    {
                        // eliminated: follow a squadmate until the respawn (1-4 / click / tap switches)
                        var kbs = Keyboard.current; var ms = Mouse.current; var tsc = Touchscreen.current;
                        if (kbs != null)
                        {
                            if (kbs.digit1Key.wasPressedThisFrame) _matchView.SwitchSpectate(0);
                            if (kbs.digit2Key.wasPressedThisFrame) _matchView.SwitchSpectate(1);
                            if (kbs.digit3Key.wasPressedThisFrame) _matchView.SwitchSpectate(2);
                            if (kbs.digit4Key.wasPressedThisFrame) _matchView.SwitchSpectate(3);
                        }
                        if ((ms != null && ms.leftButton.wasPressedThisFrame) || (tsc != null && tsc.primaryTouch.press.wasPressedThisFrame))
                            _matchView.SwitchSpectate(-1);
                        var sp = _matchView.SpectatePos;
                        if (sp.HasValue) target = sp.Value;
                        else target += Vector3.up * 2f;
                    }
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
            float tMax = GameConfig.Current(_match.Predicted.Look.Weapon, _match.Latest?.Self?.Fists ?? false).Range + tMin + 5f;
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

        // ---- squad lobby backdrop: the painted scene, filling the screen behind the squad (camera renders only the lobby layer)
        private SpriteRenderer _backdrop;
        private int _lobbyShot;   // which painted-lobby shot the camera is on (0 = none): changing shots cuts, never glides

        /// <summary>Where the painted platform's centre is in lobby_bg.png (0..1, y up).</summary>
        private static readonly Vector2 PlatformUV = new Vector2(0.5f, 0.16f);   // dockyard lobby: the open paved floor in front of the gate

        // floating light motes in front of the painted scene + a slow camera breath: the lobby feels alive, not a picture
        private ParticleSystem _motes;

        private void UpdateLobbyFx(bool on)
        {
            if (on && _motes == null)
            {
                var go = new GameObject("LobbyMotes");
                go.transform.SetParent(Cam.transform, false);
                go.transform.localPosition = new Vector3(0, 0, 6f);
                go.layer = Stage.LobbyLayer;
                _motes = go.AddComponent<ParticleSystem>();
                _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _motes.main;
                main.loop = true; main.duration = 5f; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.08f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.8f, 0.55f), new Color(1f, 0.85f, 0.55f, 0.5f));
                main.maxParticles = 90;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = _motes.emission; em.rateOverTime = 12f;
                var sh = _motes.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(12f, 6f, 6f);
                var vel = _motes.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.2f); vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                var col = _motes.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
                col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.material = MaterialLib.Unlit(Color.white, MaterialLib.Blend.Additive, Veil.UI.UIKit.Glow.texture);
                _motes.Play();
            }
            if (_motes != null && _motes.gameObject.activeSelf != on) _motes.gameObject.SetActive(on);
        }

        private void UpdateBackdrop()
        {
            bool on = (State == AppState.Menu && (Stage.SquadMode || Stage.SoloMode)) || State == AppState.Results;
            if (on && _backdrop == null)
            {
                var tex = Resources.Load<Texture2D>("UI/lobby_bg");
                if (tex != null)
                {
                    var go = new GameObject("LobbyBackdrop");
                    go.transform.SetParent(Cam.transform, false);
                    go.layer = Stage.LobbyLayer;
                    _backdrop = go.AddComponent<SpriteRenderer>();
                    _backdrop.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.height);
                    _backdrop.color = new Color(0.93f, 0.93f, 0.95f);   // keep it just under the bloom threshold
                    _backdrop.sortingOrder = -100;
                }
            }
            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(on);
                if (on)
                {
                    // the painted platform (PlatformUV in the image) is kept right under the heroes' feet on every tab,
                    // while the image still covers the whole screen (crop, never letterbox)
                    const float D = 14f;
                    float h = 2f * D * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad), w = h * Cam.aspect;
                    float ar = _backdrop.sprite.rect.width / _backdrop.sprite.rect.height;
                    Vector3 vp = Cam.WorldToViewportPoint(Stage.GroupFeet());
                    float px = (vp.x - 0.5f) * w, py = (vp.y - 0.5f) * h;
                    float ox = PlatformUV.x - 0.5f, oy = PlatformUV.y - 0.5f;     // platform offset from the image centre (fraction)
                    float H = Mathf.Max(h, w / ar) * 1.02f;
                    float cx = 0, cy = 0;
                    for (int it = 0; it < 80; it++)
                    {
                        cx = px - ox * H * ar; cy = py - oy * H;
                        if (H * ar * 0.5f >= Mathf.Abs(cx) + w * 0.5f && H * 0.5f >= Mathf.Abs(cy) + h * 0.5f) break;
                        H *= 1.03f;
                    }
                    _backdrop.transform.localPosition = new Vector3(cx, cy, D);
                    _backdrop.transform.localRotation = Quaternion.identity;
                    _backdrop.transform.localScale = new Vector3(H, H, 1);
                    UpdateLobbyFx(true);
                }
                else UpdateLobbyFx(false);
            }
            Cam.cullingMask = on ? (1 << Stage.LobbyLayer) : ~0;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && State == AppState.Match && _match != null && !_match.Driver.IsOnline && !_paused) SetPaused(true);
        }

        private void OnApplicationQuit()
        {
            if (Net != null && Net.Status == VeilNetClient.State.Connected) Net.Disconnect();
            Gateway?.Disconnect();
            Voice?.Dispose();
        }

        // ------------------------------------------------------------------ autotest (screenshots for CI / review)

        /// <summary>-mapshot -shotdir DIR: renders the island from above (top-down + oblique) for map reviews, then quits.</summary>
        private IEnumerator MapShots()
        {
            yield return new WaitForSeconds(1.5f);
            
            if (Canvas != null) Canvas.gameObject.SetActive(false);
            Stage?.SetVisible(false);
            var go = new GameObject("MapShotCam");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.45f, 0.62f);
            foreach (var c in Camera.allCameras) if (c != cam) c.enabled = false;
            void Pose(Vector3 pos, Vector3 look, bool ortho, float size)
            {
                cam.orthographic = ortho; cam.orthographicSize = size; cam.fieldOfView = 40f;
                go.transform.position = pos; go.transform.LookAt(look);
            }
            Pose(new Vector3(0, 600, 0), Vector3.zero, true, 215f); go.transform.rotation = Quaternion.Euler(90, 0, 0);   // north up
            yield return Shot("map_topdown");
            var seaGo = GameObject.Find("Sea"); if (seaGo) seaGo.SetActive(false);
            yield return Shot("map_topdown_nosea");
            if (seaGo) seaGo.SetActive(true);
            Pose(new Vector3(0, 330, -380), new Vector3(0, 0, 10), false, 0);
            yield return Shot("map_oblique");
            Pose(new Vector3(0, 70, -150), new Vector3(0, 10, 0), false, 0);
            yield return Shot("map_city");
            // collision debug: red boxes on every landmark solid
            var dbg = Veil.View.MaterialLib.Unlit(new Color(1f, 0.1f, 0.1f, 0.35f), Veil.View.MaterialLib.Blend.Alpha);
            foreach (var o in Map.Obstacles)
            {
                if (o.Kind == ObstacleKind.Water) continue;
                var size = o.Shape == ShapeKind.Box ? new Vector3(o.Half.X * 2, o.Height, o.Half.Y * 2) : new Vector3(o.Radius * 2, o.Height, o.Radius * 2);
                Veil.View.Build.Part(World.Root, o.Shape == ShapeKind.Box ? Veil.View.MeshGen.Box : Veil.View.MeshGen.Cylinder(12), dbg, new Vector3(o.Center.X, o.Height / 2, o.Center.Y), size, Quaternion.Euler(0, o.Rot, 0), "DebugSolid", false);
            }
            void Near(Vector3 at, float yaw, float dist, float h) => Pose(at + Quaternion.Euler(0, yaw, 0) * new Vector3(0, h, -dist), at + Vector3.up * 3f, false, 0);
            Near(new Vector3(0, 0, 42), 180, 26, 14); yield return Shot("lm_market");
            Near(new Vector3(0, 0, 0), 200, 22, 12); yield return Shot("lm_tower");
            { var bp = Vec2.FromYaw(22.5f + 2f) * 118f; Near(new Vector3(bp.X, 0, bp.Y), 300, 14, 7); yield return Shot("lm_bridge"); }
            Near(new Vector3(0, 0, 30), 180, 14, 6); yield return Shot("lm_street");
            Near(new Vector3(-138, 0, 0), 90, 26, 14); yield return Shot("lm_reactor");
            Near(new Vector3(138, 0, 0), 270, 36, 18); yield return Shot("lm_ruins");
            Near(new Vector3(21, 0, 160), 0, 28, 12); yield return Shot("lm_vault");
            void Top(float x, float z, float size, string name) { Pose(new Vector3(x, 300, z), new Vector3(x, 0, z), true, size); go.transform.rotation = Quaternion.Euler(90, 0, 0); }
            Top(0, 0, 70, "audit_city"); yield return Shot("audit_city");
            { var dk = Vec2.FromYaw(45) * 135f; Top(dk.X, dk.Y, 45, "a"); yield return Shot("audit_dock"); }
            { var bc = Vec2.FromYaw(135) * 140f; Top(bc.X, bc.Y, 45, "a"); yield return Shot("audit_beach"); }
            { var sn = Vec2.FromYaw(0) * 140f; Top(sn.X, sn.Y, 45, "a"); yield return Shot("audit_snow"); }
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

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

        /// <summary>Gateway → room → queue (solo squad, bots fill) → match with scripted input → results → quit.</summary>
        private IEnumerator OnlineTest()
        {
            yield return new WaitForSeconds(2f);
            Profile.UseServerForSession(_onlineHost);   // "127.0.0.1", "host:port" or "udp://host:port" (not saved)
            Profile.MatchMinutes = 1;
            GoMenu(0);
            _menu.Squad.SetMode(true);
            float t = 0;
            while (!Gateway.Online && t < 15) { t += Time.deltaTime; yield return null; }
            Debug.Log($"[VEIL] online test: gateway {Gateway.Status} as {Gateway.Handle} {Gateway.LastError}");
            if (!Gateway.Online) { Application.Quit(); yield break; }
            bool created = false;
            Gateway.CreateRoom((ok, err) => { created = ok; Debug.Log($"[VEIL] online test: create room {ok} {err}"); });
            t = 0; while (Gateway.Party.Empty && t < 5) { t += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1f);
            yield return Shot("o0_party");
            Friends().Show(true);
            yield return new WaitForSeconds(0.8f);
            yield return Shot("o0_friends");
            Friends().Show(false);
            Gateway.StartQueue(60, (ok, err) => Debug.Log($"[VEIL] online test: queue {ok} {err}"));
            yield return new WaitForSeconds(0.6f);
            yield return Shot("o0_queue");
            t = 0; while (State != AppState.Match && t < 30) { t += Time.deltaTime; yield return null; }
            if (State != AppState.Match) { Debug.Log("[VEIL] online test: no match"); Application.Quit(); yield break; }
            _match.ScriptedInput = new ScriptedPilot(_match).Next;
            Debug.Log($"[VEIL] online test: match started, squad {(char)('A' + (Gateway.Assignment?.squad ?? 0))}");
            yield return new WaitForSeconds(10f);
            yield return Shot("o1_online");
            LogPrediction();
            yield return new WaitForSeconds(10f);
            yield return Shot("o2_online");
            bool logged = false;
            while (State == AppState.Match) { if (!logged && _match != null && _match.Ended) { LogPrediction(); logged = true; } yield return null; }
            yield return new WaitForSeconds(2.5f);
            yield return Shot("o3_results");
            FetchProfile(p => Debug.Log($"[VEIL] online test: after match level {p.level} xp {p.xp} rating {p.rating} matches {p.matches} best {p.bestScore}"), e => Debug.Log("[VEIL] profile fetch failed " + e));
            yield return new WaitForSeconds(1f);
            GoMenu(0);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("o4_back_in_party");
            Debug.Log($"[VEIL] online test done (party phase {Gateway.Party.phase}, members {Gateway.Party.members.Count})");
            Gateway.Disconnect();
            Application.Quit();
        }

        private FriendsDrawer Friends() => _menu.Friends;

        private bool _walkTest;

        /// <summary>-walkpreview: frames of every hero walking, side view then front view (for comparing walk cycles).</summary>
        private IEnumerator WalkPreviewTest()
        {
            yield return new WaitForSeconds(2f);
            GoMenu(0);
            yield return new WaitForSeconds(0.5f);
            Canvas.gameObject.SetActive(false);
            _lineupShot = true;
            foreach (var (yaw, tag, aim) in new[] { (90f, "side", false), (0f, "front", false), (90f, "aim", true) })
            {
                Stage.WalkPreview(yaw, aim);
                yield return new WaitForSeconds(1.2f);
                for (int k = 0; k < 8; k++) { yield return Shot($"walk_{tag}_{k}"); yield return new WaitForSeconds(0.12f); }
            }
            Application.Quit();
        }

        private IEnumerator AutoTest()
        {
            Debug.Log("[VEIL] autotest start");
            yield return new WaitForSeconds(3f);
            yield return Shot("01_title");
            GoMenu(0);
            yield return new WaitForSeconds(0.12f);
            yield return Shot("02a_lobby_enter");   // mid entrance animation
            yield return new WaitForSeconds(2.4f);
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
            _menu.SelectTab(2);
            yield return new WaitForSeconds(3f);
            yield return Shot("03b_leaderboard");
            _menu.SelectTab(3);
            yield return new WaitForSeconds(1f);
            yield return Shot("04_settings");

            Profile.MatchMinutes = 5;
            StartOfflineMatch(true);
            yield return new WaitForSeconds(6f);
            yield return Shot("05_match_start");
            yield return new WaitForSeconds(12f);
            yield return Shot("06_match_action");
            _hud?.DebugShowPuzzle();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("06b_puzzle");
            _hud?.DebugHidePuzzle();
            _match.Driver.DebugSkip(70f);
            yield return new WaitForSeconds(10f);
            yield return Shot("07_competition");
            if (_match.Driver is LocalMatchDriver ld)
            {
                ld.Sim.DebugExtraction(_match.LocalSquad);
                yield return new WaitForSeconds(3.2f);
                yield return Shot("07b_extract_final");
            }
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
        private readonly Transform[] _pedestals = new Transform[4];
        private readonly GameObject _squadStage;
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
            var podiumBody = MaterialLib.Toon(Palette.Hex("#25204a"), 0.35f, 0.6f);
            Material[] mats = { podiumBody, podiumBody, podiumBody, podiumBody };   // dark metal; the glowing top: gold = MVP, cyan = squad
            for (int i = 0; i < 4; i++)
            {
                var p = Build.Part(_root, MeshGen.Cylinder(6), mats[i], Vector3.zero, new Vector3(1.6f, 1, 1.6f), null, "Pedestal" + i);
                _pedestals[i] = p.transform;
                // glowing hex rim on top (child of a unit-height pedestal: sits at its top face)
                var rimC = i == 0 ? Palette.Gold : Palette.Hex("#4fe3ff");
                Build.Part(p.transform, MeshGen.Cylinder(6), MaterialLib.Glow(rimC, 0.8f), new Vector3(0, 0.5f, 0), new Vector3(1.06f, 0.05f, 1.06f), null, "Rim", false);
                p.SetActive(false);
            }
            // squad lobby platform: dark disc with a glowing rim, and a glow ring under each squad slot
            _squadStage = new GameObject("SquadStage");
            _squadStage.transform.SetParent(_root, false);
            var st = _squadStage.transform;
            var disc = MaterialLib.Toon(Palette.Hex("#2a2450"), 0.2f, 0.3f);
            Build.Part(st, MeshGen.Cylinder(56), disc, new Vector3(-0.75f, 0.03f, -0.15f), new Vector3(8.6f, 0.06f, 3.4f), null, "Disc", false);
            Build.Part(st, MeshGen.Torus(0.025f, 64, 8), MaterialLib.Glow(Palette.Hex("#8f6bff"), 1.3f), new Vector3(-0.75f, 0.07f, -0.15f), new Vector3(8.6f, 1f, 3.4f), null, "Rim", false);
            for (int k = 0; k < SquadSlots.Length; k++)
            {
                var c = k == 0 ? Palette.Hex("#ffd84a") : Palette.Hex("#4fe3ff");
                Build.Part(st, MeshGen.Torus(0.035f, 40, 8), MaterialLib.Glow(c, 0.9f), SquadSlots[k] + new Vector3(0, 0.03f, 0), new Vector3(0.95f, 1f, 0.95f), null, "SlotRing" + k, false);
            }
            // soft contact shadows (the painted floor can't receive real ones)
            var blobTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    blobTex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
            blobTex.Apply();
            var blob = Sprite.Create(blobTex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64);
            for (int k = 0; k < SquadSlots.Length; k++)
            {
                var b = new GameObject("Blob" + k);
                b.transform.SetParent(st, false);
                b.transform.localPosition = SquadSlots[k] + new Vector3(0, 0.02f, 0);
                b.transform.localRotation = Quaternion.Euler(90, 0, 0);
                b.transform.localScale = new Vector3(1.5f, 0.9f, 1);
                var sr = b.AddComponent<SpriteRenderer>();
                sr.sprite = blob;
                sr.color = new Color(0, 0, 0, 0.55f);
            }
            _squadStage.SetActive(false);
        }

        public void SetVisible(bool v) => _root.gameObject.SetActive(v);

        /// <summary>Middle of the visible heroes' feet (the painted lobby keeps its platform under this point).</summary>
        public Vector3 GroupFeet()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var r in _rigs) if (r.gameObject.activeSelf) { sum += r.transform.position; n++; }
            return n > 0 ? sum / n : _root.position;
        }

        /// <summary>World point above a stage character's head (for UI labels).</summary>
        public Vector3 HeadPoint(int i) => _rigs[i].transform.position + Vector3.up * 2.6f;

        private static readonly float[] LineX = { 0, -1.35f, 1.35f, -2.6f, 2.6f };

        public void LobbyPose(Appearance mine)
        {
            _podium = false;
            SquadMode = false;
            SoloMode = false;
            _squadStage.SetActive(false);
            SetLayer(_root, 0);
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

        // squad lobby: you centre-front, squadmates left, right and far right (stage local +X is screen-left)
        // dockyard lobby: you centre-front, squadmates left and right a step behind, the 4th further out (stage +X = screen-left)
        private static readonly Vector3[] SquadSlots = { new Vector3(0, 0, 0.35f), new Vector3(1.75f, 0, -0.55f), new Vector3(-1.75f, 0, -0.55f), new Vector3(3.2f, 0, -1.1f) };
        public static Vector3[] Slots => SquadSlots;
        public const int LobbyLayer = 9;   // squad lobby renders only this layer, in front of the painted backdrop
        public bool SquadMode { get; private set; }

        /// <summary>Shows the squad (up to 4 looks, index 0 = you) standing in the lobby in their hero stance.</summary>
        public void SquadPose(IList<Appearance> looks)
        {
            _podium = false;
            SquadMode = true;
            SoloMode = false;
            _squadStage.SetActive(true);
            _squadStage.transform.Find("Blob0").localPosition = SquadSlots[0] + new Vector3(0, 0.02f, 0);
            for (int i = 0; i < _rigs.Length; i++)
            {
                bool on = i < looks.Count && i < SquadSlots.Length;
                _rigs[i].gameObject.SetActive(on);
                if (!on) continue;
                if (!_rigs[i].Look.Equals(looks[i])) _rigs[i].Rebuild(looks[i]);
                // alone: stand in the middle of the platform; with a squad: third from the left, slightly in front
                Vector3 slot = SquadSlots[i];
                _rigs[i].transform.localPosition = slot;
                _squadStage.transform.Find("Blob" + i).localPosition = slot + new Vector3(0, 0.02f, 0);
                SetLayer(_rigs[i].transform, LobbyLayer);
                _rigs[i].transform.localRotation = Quaternion.Euler(0, i == 0 ? 0f : -SquadSlots[i].x * 6f, 0);
                _rigs[i].ResetPose();
            }
            foreach (var p in _pedestals) p.gameObject.SetActive(false);
            for (int k = 0; k < SquadSlots.Length; k++)
            {
                _squadStage.transform.Find("SlotRing" + k).gameObject.SetActive(false);
                _squadStage.transform.Find("Blob" + k).gameObject.SetActive(k < looks.Count);
            }
            // the painted backdrop has its own platform: hide the 3D disc and rim
            _squadStage.transform.Find("Disc").gameObject.SetActive(false);
            _squadStage.transform.Find("Rim").gameObject.SetActive(false);
            SetLayer(_squadStage.transform, LobbyLayer);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        public bool SoloMode { get; private set; }

        /// <summary>Only your hero on the painted lobby platform (Characters / Leaderboard / Settings tabs).</summary>
        public void SoloPose(Appearance mine, bool visible = true)
        {
            _podium = false;
            SquadMode = false;
            SoloMode = true;
            for (int i = 1; i < _rigs.Length; i++) _rigs[i].gameObject.SetActive(false);
            _rigs[0].gameObject.SetActive(visible);
            if (!_rigs[0].Look.Equals(mine)) _rigs[0].Rebuild(mine);
            _rigs[0].transform.localPosition = new Vector3(0, 0, 0.3f);
            _rigs[0].transform.localRotation = Quaternion.Euler(0, 12, 0);
            _rigs[0].ResetPose();
            foreach (var p in _pedestals) p.gameObject.SetActive(false);
            _squadStage.SetActive(true);
            foreach (Transform c in _squadStage.transform) c.gameObject.SetActive(false);
            var blob = _squadStage.transform.Find("Blob0");
            blob.localPosition = new Vector3(0, 0.02f, 0.3f);
            blob.gameObject.SetActive(visible);
            SetLayer(_rigs[0].transform, LobbyLayer);
            SetLayer(_squadStage.transform, LobbyLayer);
        }

        /// <summary>Testing: all five heroes walk in place side by side (yaw: 90 = side view, 0 = towards the camera).</summary>
        public void WalkPreview(float yaw, bool aim = false)
        {
            LobbyPose(Appearance.Preset(0));
            _walkPreview = true;
            _aimPreview = aim;
            for (int i = 0; i < 5; i++)
            {
                _rigs[i].Rebuild(Appearance.Preset(i));
                _rigs[i].transform.localPosition = new Vector3(LineX[i], 0, 0);
                _rigs[i].transform.localRotation = Quaternion.Euler(0, yaw, 0);
            }
        }

        private bool _walkPreview, _aimPreview;

        public void UpdateLook(Appearance mine)
        {
            _rigs[0].Rebuild(mine);
            if (SquadMode || SoloMode) SetLayer(_rigs[0].transform, LobbyLayer);   // rebuilt parts must stay on the lobby layer
        }

        /// <summary>Results: the winning squad's players on glowing podiums — MVP (index 0) on the tall gold one in the
        /// middle, squadmates either side. Index order = name-tag order in ResultsScreen.</summary>
        public void Podium(List<PlayerResult> squad)
        {
            _podium = true;
            SquadMode = false;
            SoloMode = false;
            _squadStage.SetActive(false);
            SetLayer(_root, 0);
            // stage faces the camera: local +X is screen-left. MVP centre, then right, left, far right
            float[] x = { 0f, -1.55f, 1.55f, -3.1f };
            float[] h = { 0f, 0f, 0f, 0f };
            float[] z = { 0f, 0.3f, 0.3f, 0.6f };
            int n = Mathf.Min(1, squad.Count);   // close-up: only the MVP
            for (int i = 0; i < 5; i++) _rigs[i].gameObject.SetActive(i < n);
            for (int i = 0; i < 4; i++) _pedestals[i].gameObject.SetActive(false);   // close-up: no podium
            for (int i = 0; i < n; i++)
            {
                _rigs[i].Rebuild(squad[i].Look);
                _rigs[i].transform.localPosition = new Vector3(x[i], h[i], z[i]);
                _rigs[i].transform.localRotation = Quaternion.Euler(0, 18f, 0);   // facing the close-up camera
                _rigs[i].ResetPose();
                _pedestals[i].localPosition = new Vector3(x[i], h[i] / 2, z[i]);
                _pedestals[i].localScale = new Vector3(1.45f, h[i], 1.45f);
                // in front of the painted island deck (the camera renders only the lobby layer here)
                SetLayer(_rigs[i].transform, LobbyLayer); SetLayer(_pedestals[i], LobbyLayer);
            }
        }

        public void Update(float dt)
        {
            if (!_root.gameObject.activeSelf) return;
            _t += dt;
            for (int i = 0; i < 5; i++)
            {
                if (!_rigs[i].gameObject.activeSelf) continue;
                if (_walkPreview) _rigs[i].Animate(new RigState { Grounded = true, Aiming = _aimPreview, Velocity = _aimPreview ? Vector3.zero : _rigs[i].transform.forward * 2.2f }, dt);
                else _rigs[i].Animate(new RigState { Grounded = true, Idle = true, Victory = false, Aiming = !_podium && !SquadMode && !SoloMode && i == 2 && Mathf.Repeat(_t, 6f) < 2f }, dt);
            }
            if (GameApp.I != null && GameApp.I.State == GameApp.AppState.Menu && Mouse.current != null && Mouse.current.rightButton.isPressed)
                _rigs[0].transform.Rotate(0, -Mouse.current.delta.ReadValue().x * 0.4f, 0);
        }
    }
}
