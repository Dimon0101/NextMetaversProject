using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;
using VRCStation = VRC.SDK3.Components.VRCStation;

namespace NextMetavers.EditorTools
{
    public static class BoardTests
    {
        private const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
        private const string RunningKey = "NextMetavers.BoardTests.Running";
        private const string StepKey = "NextMetavers.BoardTests.Step";
        private const string ExitKey = "NextMetavers.BoardTests.Exit";
        private const string FailuresKey = "NextMetavers.BoardTests.Failures";
        private const string PassedKey = "NextMetavers.BoardTests.Passed";
        private const string DeadlineKey = "NextMetavers.BoardTests.Deadline";
        private const string VideoUrl = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

        private static int _waitFrames;
        private static int _cameraAttempts;

        private static bool AndroidTarget => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
        private static UdonBehaviour _board;
        private static UdonBehaviour _personal;
        private static UdonBehaviour _seatTablet;
        private static UdonBehaviour _seatStation;
        private static GameObject _seatTabletObject;
        private static GameObject _personalPanel;
        private static Transform _seatAnchor;
        private static VRCUrlInputField _lecternUrl;
        private static Slider _lecternVolume;
        private static Slider _tabletVolume;
        private static AudioSource _speaker;
        private static GameObject _boardVideoScreen;
        private static Renderer _boardSlideScreen;
        private static GameObject _lecternControls;
        private static UdonBehaviour _draw;
        private static LineRenderer[] _strokeLines;
        private static GameObject _boardDrawScreen;
        private static Transform _lecternBoardTab;
        private static Transform _lecternDrawTab;

        private static readonly Action[] Steps =
        {
            () => _waitFrames = 180,
            Locate,
            CheckInitialState,
            CheckEmptyUrl,
            CheckLecternVideo,
            CheckSlides,
            CheckScreensEverywhere,
            CheckSlideNavigation,
            CheckDeserialization,
            CheckTabletWithoutRights,
            CheckLecternWorksWhileHostIsOther,
            CheckOpenAccess,
            CheckRequestFlow,
            CheckStatusShowsHost,
            CheckSeatTablet,
            CheckVolume,
            CheckTurnOffHidesEverything,
            CheckTabs,
            CheckDrawSetup,
            CheckDrawWithoutRights,
            CheckDrawStroke,
            CheckDrawShapes,
            CheckDrawUndoAndClear,
            CheckDrawEraser,
            CheckDrawSync,
            CheckCrossPlatform,
            CheckDrawCameraOnDemand,
            CheckDrawCameraStops,
            StartRealVideo,
            WaitForVideo,
        };

        [MenuItem("NextMetavers/Дошка/Перевірити дошку")]
        public static void RunFromMenu()
        {
            Begin(false);
        }

        public static void RunFromCommandLine()
        {
            Begin(true);
        }

        private static void Begin(bool exitWhenDone)
        {
            SessionState.SetBool(RunningKey, true);
            SessionState.SetBool(ExitKey, exitWhenDone);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetInt(PassedKey, 0);
            SessionState.SetString(FailuresKey, "");
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (_waitFrames > 0)
            {
                _waitFrames--;
                return;
            }

            int step = SessionState.GetInt(StepKey, 0);
            SessionState.SetInt(StepKey, step + 1);

            if (step >= Steps.Length)
            {
                Finish();
                return;
            }

            Steps[step]();
        }

        private static void Locate()
        {
            GameObject boardObject = GameObject.Find("PresentationBoard");
            Check(boardObject != null, "об'єкт PresentationBoard знайдено");
            if (boardObject == null)
            {
                SessionState.SetInt(StepKey, Steps.Length);
                return;
            }

            _board = boardObject.GetComponent<UdonBehaviour>();
            _boardVideoScreen = boardObject.transform
                .Find(AndroidTarget ? "Screens/VideoScreenAVPro" : "Screens/VideoScreen").gameObject;
            _boardSlideScreen = boardObject.transform.Find("Screens/SlideScreen").GetComponent<Renderer>();
            _speaker = boardObject.GetComponentInChildren<AudioSource>(true);

            Transform lecternCanvas = GameObject.Find("BoardControlPanel").transform.Find("Canvas");
            _lecternControls = lecternCanvas.Find("Controls").gameObject;
            _lecternUrl = lecternCanvas.GetComponentInChildren<VRCUrlInputField>(true);
            _lecternVolume = lecternCanvas.Find("VolumeSlider").GetComponent<Slider>();

            GameObject personalObject = GameObject.Find("PersonalTablet");
            _personal = personalObject.GetComponent<UdonBehaviour>();
            _personalPanel = personalObject.transform.Find("Panel").gameObject;
            _tabletVolume = _personalPanel.transform.Find("Canvas/Controls/VolumeSlider").GetComponent<Slider>();

            Transform seats = GameObject.Find("SeatTablets").transform;
            _seatTabletObject = seats.Find("SeatTablet (1)").gameObject;
            _seatAnchor = seats.Find("SeatAnchor (1)");
            _seatTablet = _seatTabletObject.transform.Find("Body").GetComponent<UdonBehaviour>();
            _seatStation = Object.FindObjectsOfType<VRCStation>(true)
                .SelectMany(station => station.GetComponents<UdonBehaviour>())
                .FirstOrDefault(udon => udon.GetProgramVariable("tablet") as GameObject == _seatTabletObject);

            GameObject drawArea = GameObject.Find("BoardDrawArea");
            _draw = drawArea == null ? null : drawArea.GetComponent<UdonBehaviour>();
            _strokeLines = drawArea == null ? new LineRenderer[0] : drawArea.GetComponentsInChildren<LineRenderer>(true);
            _boardDrawScreen = boardObject.transform.Find("Screens/DrawScreen").gameObject;
            _lecternBoardTab = lecternCanvas.Find("Controls/BoardTab");
            _lecternDrawTab = lecternCanvas.Find("Controls/DrawTab");

            Check(_board != null, "UdonBehaviour дошки знайдено");
            Check(_personal != null, "UdonBehaviour планшета знайдено");
            Check(_seatTablet != null, "UdonBehaviour планшета крісла знайдено");
            Check(_seatStation != null, "UdonBehaviour крісла знайдено");
            Check(_lecternUrl != null && _lecternVolume != null, "поле посилання і гучність на столі знайдено");
            Check(_tabletVolume != null, "гучність планшета лежить у блоці керування");
            Check(Utilities.IsValid(Networking.LocalPlayer), "ClientSim створив локального гравця");

            float distance = Vector3.Distance(Networking.LocalPlayer.GetPosition(), GameObject.Find("BoardControlPanel").transform.position);
            Check(distance > 3f, $"гравець стоїть далеко від столу ({distance:0.0} м) — як при звичайному тесті");
        }

        private static void CheckInitialState()
        {
            Check(Mode() == 0, "на старті дошка вимкнена");
            Check(!_boardVideoScreen.activeInHierarchy, "екран відео сховано");
            Check(!_boardSlideScreen.gameObject.activeInHierarchy, "екран слайдів сховано");
            Check(_lecternControls.activeInHierarchy, "керування на столі видно");
            Check(!_personalPanel.activeInHierarchy, "панель планшета закрита");
            Check(!_seatTabletObject.activeInHierarchy, "планшет крісла сховано");
            Check(Int("_hostId") == -1 && Int("_requestId") == -1, "ведучого і запитів немає");
        }

        private static void CheckEmptyUrl()
        {
            _board.SendCustomEvent("PlayVideoFromUrl");
            Check(Mode() == 0, "без посилання режим відео не вмикається");
            Check(StatusText().Contains("вставте посилання"), "без посилання показано підказку");
        }

        private static void CheckLecternVideo()
        {
            _lecternUrl.SetUrl(new VRCUrl(VideoUrl));
            _board.SendCustomEvent("PlayVideoFromUrl");

            Check(Mode() == 1, "кнопка на столі вмикає режим відео");
            Check(Int("_urlVersion") == 1, "посилання прийнято");
            Check(Int("_loadedUrlVersion") == 1, "почалося завантаження відео");
            Check(_lecternUrl.GetUrl().Get().Length == 0, "поле посилання очищено після запуску");
        }

        private static void CheckSlides()
        {
            _board.SendCustomEvent("ShowProjectSlides");

            Check(Mode() == 2, "кнопка слайдів вмикає режим слайдів");
            Check(_boardSlideScreen.gameObject.activeInHierarchy, "екран слайдів показано");
            Check(_boardSlideScreen.material.mainTexture != null, "на екран слайдів призначено картинку");
            Check(Mathf.Abs(_boardSlideScreen.transform.localScale.x - 4.356f) < 0.05f, "слайд вписано в дошку за пропорціями");
            Check(!_boardVideoScreen.activeInHierarchy, "екран відео сховано в режимі слайдів");
        }

        private static void CheckScreensEverywhere()
        {
            Renderer tabletSlide = _personalPanel.transform.Find("Screens/SlideScreen").GetComponent<Renderer>();
            Renderer seatSlide = _seatTabletObject.transform.Find("Screens/SlideScreen").GetComponent<Renderer>();

            Check(tabletSlide.gameObject.activeSelf && tabletSlide.material.mainTexture != null, "слайд дублюється на панель планшета");
            Check(seatSlide.gameObject.activeSelf && seatSlide.material.mainTexture != null, "слайд дублюється на планшет крісла");
        }

        private static void CheckSlideNavigation()
        {
            _board.SendCustomEvent("NextSlide");
            Check(Int("_slide") == 1, "гортання вперед працює");
            Check(CounterText().Contains("2 / 3"), "лічильник слайдів оновлюється");

            _board.SendCustomEvent("PrevSlide");
            Check(Int("_slide") == 0, "гортання назад працює");

            _board.SendCustomEvent("PrevSlide");
            Check(Int("_slide") == 0, "перед першим слайдом гортання не йде в мінус");

            _board.SendCustomEvent("NextSlide");
            _board.SendCustomEvent("NextSlide");
            _board.SendCustomEvent("NextSlide");
            Check(Int("_slide") == 2, "за останній слайд гортання не виходить");
        }

        private static void CheckDeserialization()
        {
            _board.SetProgramVariable("_slide", 1);
            _board.SendCustomEvent("_onDeserialization");
            Check(CounterText().Contains("2 / 3"), "стан від іншого гравця застосовується");
            Check(_boardSlideScreen.gameObject.activeInHierarchy, "після синхронізації екран лишається видимим");
        }

        private static void CheckTabletWithoutRights()
        {
            _board.SetProgramVariable("_hostId", 999);
            _board.SetProgramVariable("_openAccess", false);
            _board.SendCustomEvent("RefreshPanels");

            GameObject controls = _board.GetProgramVariable("tabletControls") as GameObject;
            GameObject hint = _board.GetProgramVariable("tabletHint") as GameObject;
            GameObject request = _board.GetProgramVariable("tabletRequestButton") as GameObject;
            Check(controls != null && !controls.activeSelf, "без прав керування на планшеті сховано");
            Check(hint != null && hint.activeSelf, "без прав видно підказку");
            Check(request != null && request.activeSelf, "без прав видно кнопку запиту");

            int slide = Int("_slide");
            int mode = Mode();
            _board.SendCustomEvent("TabletTurnOff");
            _board.SendCustomEvent("TabletNextSlide");
            _board.SendCustomEvent("TabletPrevSlide");
            _board.SendCustomEvent("TabletTogglePause");
            _board.SendCustomEvent("TabletShowProjectSlides");
            _board.SendCustomEvent("TabletPlayVideoFromUrl");
            _board.SendCustomEvent("TabletPlaySlidesFromUrl");
            _board.SendCustomEvent("TabletRestartVideo");
            Check(Mode() == mode && Int("_slide") == slide, "без прав жодна кнопка планшета не діє");
        }

        private static void CheckLecternWorksWhileHostIsOther()
        {
            _board.SendCustomEvent("TurnOff");
            Check(Mode() == 0, "стіл керує навіть коли ведучий інший гравець");

            _board.SendCustomEvent("ShowProjectSlides");
            Check(Mode() == 2, "стіл вмикає слайди без обмежень");
        }

        private static void CheckOpenAccess()
        {
            _board.SetProgramVariable("_openAccess", true);
            _board.SendCustomEvent("RefreshPanels");

            GameObject controls = _board.GetProgramVariable("tabletControls") as GameObject;
            GameObject request = _board.GetProgramVariable("tabletRequestButton") as GameObject;
            Check(controls != null && controls.activeSelf, "з відкритим доступом керування на планшеті видно");
            Check(request != null && !request.activeSelf, "з відкритим доступом кнопку запиту сховано");

            _board.SendCustomEvent("TabletTurnOff");
            Check(Mode() == 0, "з відкритим доступом кнопки планшета працюють");

            _board.SetProgramVariable("_openAccess", false);
            _board.SetProgramVariable("_hostId", -1);
            _board.SendCustomEvent("RefreshPanels");
        }

        private static void CheckRequestFlow()
        {
            int localId = Networking.LocalPlayer.playerId;
            _board.SetProgramVariable("_hostId", 999);
            _board.SendCustomEvent("RequestHost");
            Check(Int("_requestId") == localId, "запит права ведучого надіслано");

            GameObject row = _board.GetProgramVariable("requestRow") as GameObject;
            Check(row != null && row.activeSelf, "на столі зʼявився рядок запиту");

            _board.SendCustomEvent("GrantHost");
            Check(Int("_hostId") == localId, "стіл видає права ведучого");
            Check(Int("_requestId") == -1, "після видачі запит знято");
            Check(row != null && !row.activeSelf, "рядок запиту сховано");

            GameObject controls = _board.GetProgramVariable("tabletControls") as GameObject;
            Check(controls != null && controls.activeSelf, "ведучий бачить керування на планшеті");

            _board.SendCustomEvent("TabletShowProjectSlides");
            Check(Mode() == 2, "ведучий керує з планшета");

            _board.SendCustomEvent("RevokeHost");
            Check(Int("_hostId") == -1, "стіл силою забирає права");
            Check(controls != null && !controls.activeSelf, "після зняття прав керування на планшеті зникає");

            _board.SendCustomEvent("RequestHost");
            _board.SendCustomEvent("DenyRequest");
            Check(Int("_requestId") == -1, "стіл відхиляє запит");
        }

        private static void CheckStatusShowsHost()
        {
            int localId = Networking.LocalPlayer.playerId;
            _board.SetProgramVariable("_hostId", localId);
            _board.SendCustomEvent("RefreshPanels");
            Check(StatusText().Contains(Networking.LocalPlayer.displayName), "у статусі видно імʼя ведучого");

            _board.SetProgramVariable("_hostId", -1);
            _board.SendCustomEvent("RefreshPanels");
            Check(StatusText().Contains("немає"), "без ведучого статус це показує");
        }

        private static void CheckSeatTablet()
        {
            _seatStation.SetProgramVariable("onStationEnteredPlayer", Networking.LocalPlayer);
            _seatStation.SendCustomEvent("_onStationEntered");
            Check(_seatTabletObject.activeSelf, "після посадки планшет зʼявляється");

            _seatTablet.SendCustomEvent("_interact");
            Check(_personalPanel.activeSelf, "натиск на планшет відкриває панель");
            Check(Vector3.Distance(_personal.transform.position, _seatAnchor.position) < 0.01f, "панель стає на місце біля крісла");
            Check(!_seatTabletObject.activeSelf, "поки панель відкрита, планшет сховано");

            _personal.SendCustomEvent("Hide");
            Check(!_personalPanel.activeSelf, "кнопка закриття ховає панель");
            Check(_seatTabletObject.activeSelf, "після закриття планшет повертається");

            _seatTablet.SendCustomEvent("_interact");
            _seatTablet.SendCustomEvent("_interact");
            Check(!_personalPanel.activeSelf && _seatTabletObject.activeSelf, "повторний натиск на планшет закриває панель");

            _seatStation.SetProgramVariable("onStationExitedPlayer", Networking.LocalPlayer);
            _seatStation.SendCustomEvent("_onStationExited");
            Check(!_seatTabletObject.activeSelf, "коли гравець встав, планшет зникає");
            Check(!_personalPanel.activeSelf, "коли гравець встав, панель закрита");
        }

        private static void CheckVolume()
        {
            _lecternVolume.value = 0.3f;
            _board.SendCustomEvent("VolumeChanged");
            Check(Mathf.Abs(_speaker.volume - 0.3f) < 0.01f, "повзунок на столі змінює гучність");

            _tabletVolume.value = 0.55f;
            _board.SendCustomEvent("VolumeChanged");
            Check(Mathf.Abs(_speaker.volume - 0.55f) < 0.01f, "повзунок на планшеті змінює гучність");
            Check(Mathf.Abs(_lecternVolume.value - 0.55f) < 0.01f, "повзунки на панелях показують однакове значення");

            _lecternVolume.value = 0.8f;
            _board.SendCustomEvent("VolumeChanged");
        }

        private static void CheckTurnOffHidesEverything()
        {
            _board.SendCustomEvent("ShowProjectSlides");
            _board.SendCustomEvent("TurnOff");

            Renderer tabletSlide = _personalPanel.transform.Find("Screens/SlideScreen").GetComponent<Renderer>();
            Renderer seatSlide = _seatTabletObject.transform.Find("Screens/SlideScreen").GetComponent<Renderer>();
            Check(!_boardSlideScreen.gameObject.activeSelf, "після вимкнення екран дошки сховано");
            Check(!tabletSlide.gameObject.activeSelf, "після вимкнення екран панелі сховано");
            Check(!seatSlide.gameObject.activeSelf, "після вимкнення екран планшета крісла сховано");
        }

        private static void CheckTabs()
        {
            _board.SendCustomEvent("ShowDrawTab");
            Check(_lecternDrawTab.gameObject.activeSelf && !_lecternBoardTab.gameObject.activeSelf, "вкладка малювання відкривається");

            _board.SendCustomEvent("ShowBoardTab");
            Check(_lecternBoardTab.gameObject.activeSelf && !_lecternDrawTab.gameObject.activeSelf, "вкладка дошки повертається");
        }

        private static void CheckDrawSetup()
        {
            Check(_draw != null, "UdonBehaviour малювання знайдено");
            Check(_strokeLines.Length == 120, $"пул ліній створено ({_strokeLines.Length})");
            Check(_boardDrawScreen.activeInHierarchy, "шар малюнка на дошці увімкнено");

            var camera = GameObject.Find("BoardDrawArea").GetComponentInChildren<Camera>(true);
            Check(camera != null && camera.targetTexture != null, "камера малювання пише в текстуру");
            Check(camera != null && camera.orthographic && camera.cullingMask == 1 << 24, "камера бачить лише область малювання");

            Transform[] surfaces = _draw.GetProgramVariable("surfaces") as Transform[];
            Check(surfaces != null && surfaces.Length == 2, "поверхні для малювання: дошка і планшет");
            Check(surfaces != null && surfaces[0] != null && surfaces[0].gameObject.layer == 17, "поверхню дошки винесено на шар Walkthrough");
            Check(surfaces != null && surfaces[1] != null && surfaces[1].GetComponent<BoxCollider>() != null, "у поверхні планшета є колайдер");
        }

        private static void CheckDrawWithoutRights()
        {
            _board.SetProgramVariable("_hostId", 999);
            _board.SetProgramVariable("_openAccess", false);
            _board.SendCustomEvent("RefreshPanels");

            Stroke(new Vector2(-0.2f, -0.1f), new Vector2(0.2f, 0.1f));
            Check(DrawInt("_strokes") == 0, "без прав малювати не можна");
        }

        private static void CheckDrawStroke()
        {
            _board.SetProgramVariable("_hostId", Networking.LocalPlayer.playerId);
            _board.SendCustomEvent("RefreshPanels");

            _draw.SendCustomEvent("ToolBrushMode");
            _draw.SendCustomEvent("WidthThick");
            _draw.SendCustomEvent("Color2");
            Stroke(new Vector2(-0.3f, -0.2f), new Vector2(0.3f, 0.25f));

            Check(DrawInt("_strokes") == 1, "штрих намальовано");
            Check(DrawInt("_points") >= 3, "у штриха кілька точок");
            Check(_strokeLines[0].gameObject.activeSelf, "лінію увімкнено");
            Check(_strokeLines[0].positionCount == DrawInt("_points"), "усі точки потрапили в лінію");
            Check(Mathf.Abs(_strokeLines[0].widthMultiplier - 0.075f) < 0.001f, "товщина «товсто» застосована");
            Check(_strokeLines[0].startColor.b > _strokeLines[0].startColor.r, "обраний колір застосовано");

            Text[] toolTexts = _draw.GetProgramVariable("toolTexts") as Text[];
            Check(toolTexts != null && toolTexts[0].text.Contains("Пензель"), "підпис інструмента оновлюється");
        }

        private static void CheckDrawShapes()
        {
            _draw.SendCustomEvent("ClearAll");

            _draw.SendCustomEvent("ToolLineMode");
            Stroke(new Vector2(-0.3f, -0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_points") == 2, "лінія з двох точок");

            _draw.SendCustomEvent("ToolRectMode");
            Stroke(new Vector2(-0.3f, -0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_points") == 7, "прямокутник замкнений");

            _draw.SendCustomEvent("ToolEllipseMode");
            Stroke(new Vector2(-0.3f, -0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_points") == 40, "еліпс побудовано");

            _draw.SendCustomEvent("ToolArrowMode");
            Stroke(new Vector2(-0.3f, -0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_strokes") == 4, "стрілку додано");
            Check(_strokeLines[3].positionCount == 5, "у стрілки є вістря");
        }

        private static void CheckDrawUndoAndClear()
        {
            int strokes = DrawInt("_strokes");
            _draw.SendCustomEvent("Undo");
            Check(DrawInt("_strokes") == strokes - 1, "скасування прибирає останній штрих");
            Check(!_strokeLines[strokes - 1].gameObject.activeSelf, "лінію скасованого штриха вимкнено");

            _draw.SendCustomEvent("ClearAll");
            Check(DrawInt("_strokes") == 0 && DrawInt("_points") == 0, "очищення прибирає все");
            Check(!_strokeLines[0].gameObject.activeSelf, "усі лінії вимкнено");
        }

        private static void CheckDrawEraser()
        {
            _draw.SendCustomEvent("ToolBrushMode");
            Stroke(new Vector2(-0.3f, 0f), new Vector2(-0.1f, 0f));
            Stroke(new Vector2(0.2f, 0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_strokes") == 2, "два штрихи на дошці");

            _draw.SendCustomEvent("ToolEraserMode");
            _draw.SendCustomEvent("WidthThick");
            Stroke(new Vector2(0.2f, 0.2f), new Vector2(0.3f, 0.2f));
            Check(DrawInt("_strokes") == 1, "гумка прибирає штрих під собою");

            Stroke(new Vector2(0.45f, -0.45f), new Vector2(0.48f, -0.45f));
            Check(DrawInt("_strokes") == 1, "гумка не чіпає інші штрихи");
            _draw.SendCustomEvent("ToolBrushMode");
        }

        private static void CheckDrawSync()
        {
            _draw.SendCustomEvent("ClearAll");
            Stroke(new Vector2(-0.2f, -0.2f), new Vector2(0.2f, 0.2f));

            int[] syncCount = _draw.GetProgramVariable("_syncCount") as int[];
            float[] syncX = _draw.GetProgramVariable("_syncX") as float[];
            Check(syncCount != null && syncCount.Length == DrawInt("_strokes"), "штрихи потрапили в синхронізацію");
            Check(syncX != null && syncX.Length == DrawInt("_points"), "точки потрапили в синхронізацію");

            _draw.SetProgramVariable("_syncColor", new[] { 1 });
            _draw.SetProgramVariable("_syncWidth", new[] { 0 });
            _draw.SetProgramVariable("_syncCount", new[] { 2 });
            _draw.SetProgramVariable("_syncX", new[] { -1f, 1f });
            _draw.SetProgramVariable("_syncY", new[] { -0.5f, 0.5f });
            _draw.SendCustomEvent("_onDeserialization");

            Check(DrawInt("_strokes") == 1 && DrawInt("_points") == 2, "малюнок від іншого гравця застосовано");
            Check(_strokeLines[0].positionCount == 2, "лінію перебудовано за отриманими даними");
            Check(Mathf.Abs(_strokeLines[0].GetPosition(1).x - 1f) < 0.001f, "координати точок збіглися");

            _draw.SendCustomEvent("ClearAll");
            _board.SetProgramVariable("_hostId", -1);
            _board.SendCustomEvent("RefreshPanels");
        }

        private static void Stroke(Vector2 from, Vector2 to)
        {
            _draw.SetProgramVariable("inputPoint", from);
            _draw.SendCustomEvent("_StrokeBegin");

            for (int i = 1; i <= 4; i++)
            {
                _draw.SetProgramVariable("inputPoint", Vector2.Lerp(from, to, i / 4f));
                _draw.SendCustomEvent("_StrokeMove");
            }

            _draw.SendCustomEvent("_StrokeEnd");
        }

        private static int DrawInt(string name)
        {
            object value = _draw.GetProgramVariable(name);
            return value == null ? int.MinValue : (int)value;
        }

        private static void CheckCrossPlatform()
        {
            GameObject boardObject = GameObject.Find("PresentationBoard");
            var unityPlayer = boardObject.GetComponent<VRC.SDK3.Video.Components.VRCUnityVideoPlayer>();
            var avProPlayer = boardObject.GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoPlayer>();
            Check(unityPlayer != null, "Unity-плеєр на дошці є (ПК)");
            Check(avProPlayer != null, "AVPro-плеєр на дошці є (Android)");
            Check(_board.GetProgramVariable("unityVideoPlayer") != null, "Unity-плеєр підключено до дошки");
            Check(_board.GetProgramVariable("avProVideoPlayer") != null, "AVPro-плеєр підключено до дошки");

            var avProScreens = _board.GetProgramVariable("avProScreens") as GameObject[];
            var videoScreens = _board.GetProgramVariable("videoScreens") as GameObject[];
            Check(avProScreens != null && videoScreens != null && avProScreens.Length == videoScreens.Length,
                $"екранів AVPro стільки ж, скільки звичайних ({(avProScreens == null ? 0 : avProScreens.Length)})");

            GameObject[] unused = AndroidTarget ? videoScreens : avProScreens;
            Check(unused != null && unused.All(screen => screen != null && !screen.activeSelf),
                AndroidTarget ? "в Android-збірці екрани Unity-плеєра вимкнені" : "на ПК екрани AVPro вимкнені");

            var screen = boardObject.transform.Find("Screens/VideoScreenAVPro")
                .GetComponent<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoScreen>();
            Check(screen != null, "компонент екрана AVPro на місці");
            var speaker = boardObject.GetComponentInChildren<VRC.SDK3.Video.Components.AVPro.VRCAVProVideoSpeaker>(true);
            Check(speaker != null, "динамік AVPro на місці");

            Material panel = GameObject.Find("BoardControlPanel").transform.Find("Back").GetComponent<Renderer>().sharedMaterial;
            Check(panel.shader.name == "Unlit/Color", $"панель на мобільному шейдері ({panel.shader.name})");
            Check(_boardSlideScreen.sharedMaterial.shader.name == "Unlit/Texture", "екран слайдів на Unlit-шейдері");
            Check(_boardDrawScreen.GetComponent<Renderer>().sharedMaterial.shader.name == "Unlit/Transparent", "шар малюнка на Unlit-шейдері");
        }

        private static void CheckDrawCameraOnDemand()
        {
            var camera = GameObject.Find("BoardDrawArea").GetComponentInChildren<Camera>(true);
            _board.SetProgramVariable("_hostId", Networking.LocalPlayer.playerId);
            _board.SendCustomEvent("RefreshPanels");
            _draw.SendCustomEvent("ClearAll");
            _draw.SendCustomEvent("ToolBrushMode");
            Stroke(new Vector2(-0.1f, -0.1f), new Vector2(0.1f, 0.1f));

            Check(camera.enabled, "камера малювання вмикається на час оновлення");
            _waitFrames = 5;
        }

        private static void CheckDrawCameraStops()
        {
            var camera = GameObject.Find("BoardDrawArea").GetComponentInChildren<Camera>(true);
            if (camera.enabled && _cameraAttempts < 30)
            {
                _cameraAttempts++;
                SessionState.SetInt(StepKey, SessionState.GetInt(StepKey, 0) - 1);
                _waitFrames = 20;
                return;
            }

            Check(!camera.enabled, "камера малювання вимикається, коли малюнок не змінюється");

            _draw.SendCustomEvent("ClearAll");
            _board.SetProgramVariable("_hostId", -1);
            _board.SendCustomEvent("RefreshPanels");
        }

        private static void StartRealVideo()
        {
            if (AndroidTarget)
            {
                Debug.Log("[Tests] перевірку справжнього відтворення пропущено: у редакторі AVPro не грає");
                SessionState.SetInt(StepKey, Steps.Length);
                return;
            }

            _lecternUrl.SetUrl(new VRCUrl(VideoUrl));
            _board.SendCustomEvent("PlayVideoFromUrl");
            Check(Mode() == 1, "перед перевіркою відтворення увімкнено режим відео");
            SessionState.SetFloat(DeadlineKey, Time.realtimeSinceStartup + 240f);
            _waitFrames = 30;
        }

        private static void WaitForVideo()
        {
            bool ready = (bool)_board.GetProgramVariable("_videoReady");
            float left = SessionState.GetFloat(DeadlineKey, 0f) - Time.realtimeSinceStartup;
            if (!ready && left > 0f)
            {
                SessionState.SetInt(StepKey, Steps.Length - 1);
                _waitFrames = 120;
                Debug.Log($"[Tests] чекаю відео, лишилось {left:0} с, статус: {StatusText()}");
                return;
            }

            Check(ready, "відео з YouTube завантажилося і готове до відтворення, статус: " + StatusText());
            Check(!ready || _boardVideoScreen.activeInHierarchy, "екран відео показано");
            Check(!ready || _seatTabletObject.transform.Find("Screens/VideoScreen").gameObject.activeSelf, "відео дублюється на планшети");
            _board.SendCustomEvent("TurnOff");
        }

        private static void Finish()
        {
            string failures = SessionState.GetString(FailuresKey, "");
            int passed = SessionState.GetInt(PassedKey, 0);
            int failed = failures.Length == 0 ? 0 : failures.Split('\n').Count(line => line.Length > 0);

            Debug.Log($"[Tests] пройдено {passed}, провалено {failed}\n{failures}");

            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Tick;

            if (SessionState.GetBool(ExitKey, false))
            {
                EditorApplication.Exit(failed == 0 ? 0 : 1);
                return;
            }

            EditorApplication.ExitPlaymode();
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                SessionState.SetInt(PassedKey, SessionState.GetInt(PassedKey, 0) + 1);
                Debug.Log($"[Tests] OK: {message}");
                return;
            }

            SessionState.SetString(FailuresKey, SessionState.GetString(FailuresKey, "") + "ПОМИЛКА: " + message + "\n");
            Debug.LogError($"[Tests] ПОМИЛКА: {message}");
        }

        private static string StatusText()
        {
            Text[] texts = _board.GetProgramVariable("statusTexts") as Text[];
            return texts == null || texts.Length == 0 || texts[0] == null ? "?" : texts[0].text.Replace('\n', '|');
        }

        private static string CounterText()
        {
            Text[] texts = _board.GetProgramVariable("slideCounterTexts") as Text[];
            return texts == null || texts.Length == 0 || texts[0] == null ? "?" : texts[0].text;
        }

        private static int Mode()
        {
            return Int("_mode");
        }

        private static int Int(string name)
        {
            object value = _board.GetProgramVariable(name);
            return value == null ? int.MinValue : (int)value;
        }
    }
}
