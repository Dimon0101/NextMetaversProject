using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Video.Components;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;
using VRCStation = VRC.SDK3.Components.VRCStation;

namespace NextMetavers.EditorTools
{
    public static class PresentationBoardBuilder
    {
        public const string Root = "Assets/PresentationBoard";
        public const string SlidesFolder = Root + "/Slides";
        private const string ScenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
        private const string BoardName = "PresentationBoard";
        private const string PanelName = "BoardControlPanel";
        private const string TabletName = "PersonalTablet";
        private const string SeatsName = "SeatTablets";

        private static readonly Vector3 ScreenCenter = new Vector3(0.007f, 0.39f, -0.041f);
        private static readonly Vector3 ScreenFallbackCenter = new Vector3(-0.113f, 2.774f, -10.327f);
        private static readonly Vector3 LecternFallbackPosition = new Vector3(4.158f, 0.57f, -7.512f);
        private const float LecternHeight = 1.2f;

        private static readonly Vector2 ScreenSize = new Vector2(4.356f, 2.45f);
        private static readonly Vector2 SlideMaxSize = new Vector2(4.6f, 2.45f);

        private static readonly Vector2 LecternPanelSize = new Vector2(1000f, 880f);
        private static readonly Vector2 TabletPanelSize = new Vector2(1000f, 780f);
        private const float LecternPanelScale = 0.0006f;
        private const float LecternPanelTilt = 50f;
        private const float TabletPanelScale = 0.00042f;
        private const float TabletScreenScale = 0.08f;

        private static readonly Vector3 TabletBodySize = new Vector3(0.3f, 0.21f, 0.012f);
        private const float SeatScreenScale = 0.058f;
        private const float SeatTabletDistance = 0.42f;
        private const float SeatTabletHeight = 0.88f;
        private const float SeatTabletTilt = 20f;
        private const float SeatAnchorDistance = 0.5f;
        private const float SeatAnchorHeight = 1f;
        private const float SeatAnchorTilt = 15f;

        private const string DrawAreaName = "BoardDrawArea";
        private static readonly Vector3 DrawAreaPosition = new Vector3(0f, -500f, 0f);
        private const int DrawLayer = 24;
        private const int SurfaceLayer = 17;
        private const int StrokePoolSize = 120;
        private static readonly float[] StrokeWidths = { 0.012f, 0.03f, 0.075f };
        private static readonly Color[] StrokePalette =
        {
            new Color(0.9f, 0.15f, 0.15f),
            new Color(0.1f, 0.1f, 0.12f),
            new Color(0.15f, 0.45f, 0.95f),
            new Color(0.15f, 0.7f, 0.3f),
            new Color(0.98f, 0.8f, 0.1f),
            Color.white,
        };

        private static readonly Color PanelColor = new Color(0.09f, 0.1f, 0.13f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.28f, 0.34f);
        private static readonly Color PrimaryColor = new Color(0.16f, 0.42f, 0.82f);
        private static readonly Color DangerColor = new Color(0.62f, 0.2f, 0.2f);
        private static readonly Color MutedTextColor = new Color(0.72f, 0.75f, 0.8f);

        private static Font _font;
        private static Font UiFont => _font ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private class PanelParts
        {
            public VRCUrlInputField Url;
            public Slider Volume;
            public Text Status;
            public Text Counter;
            public Text Pause;
            public Text Access;
            public Text Request;
            public GameObject VideoControls;
            public GameObject SlideControls;
            public GameObject Controls;
            public GameObject RequestRow;
            public GameObject RevokeButton;
            public GameObject Hint;
            public GameObject RequestButton;
            public GameObject BoardTab;
            public GameObject DrawTab;
            public Text ToolText;
        }

        private class ScreenSet
        {
            public GameObject Video;
            public GameObject AvPro;
            public Renderer Slide;
            public GameObject Draw;
        }

        [MenuItem("NextMetavers/Дошка/Зібрати дошку в сцені")]
        public static void Build()
        {
            EnsureFolders();
            if (!EnsureProgramAssets()) return;
            if (LoadSlides().Length == 0) GenerateDemoSlides();

            Scene scene = SceneManager.GetActiveScene();
            RemoveOldObjects(scene);

            RenderTexture videoTexture = GetVideoTexture();
            RenderTexture drawTexture = GetDrawTexture();
            Shader unlit = Shader.Find("Unlit/Texture");
            Material videoMaterial = GetMaterial("BoardVideoScreen", unlit, videoTexture, Color.white);
            Material avProMaterial = GetMaterial("BoardVideoScreenAVPro", Shader.Find("Video/RealtimeEmissiveGamma"), null, Color.white);
            Material slideMaterial = GetMaterial("BoardSlideScreen", unlit, null, Color.white);
            Material drawMaterial = GetMaterial("BoardDrawScreen", Shader.Find("Unlit/Transparent"), drawTexture, Color.white);
            Material strokeMaterial = GetMaterial("BoardStroke", Shader.Find("Sprites/Default"), null, Color.white);
            Material panelMaterial = GetMaterial("BoardPanelBack", Shader.Find("Unlit/Color"), null, PanelColor);

            BoardDrawing drawing = BuildDrawArea(drawTexture, strokeMaterial);
            UdonBehaviour drawingUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(drawing);

            Transform boardMesh = FindInScene(scene, "Projector_Table.004");
            Vector3 center = ScreenFallbackCenter;
            Vector3 normal = Vector3.forward;
            if (boardMesh != null)
            {
                center = boardMesh.TransformPoint(ScreenCenter);
                normal = boardMesh.forward;
            }
            else
            {
                Debug.LogWarning("[PresentationBoard] Projector_Table.004 не знайдено, дошку поставлено за типовими координатами");
            }

            var boardRoot = new GameObject(BoardName);
            Undo.RegisterCreatedObjectUndo(boardRoot, "Build presentation board");
            boardRoot.transform.SetPositionAndRotation(center + normal * 0.01f, Quaternion.LookRotation(-normal, Vector3.up));

            ScreenSet boardScreens = CreateScreenSet(boardRoot.transform, Vector3.zero, 1f, videoMaterial, avProMaterial, slideMaterial, drawMaterial);
            GameObject boardSurface = CreateDrawSurface(boardRoot.transform, new Vector3(0f, 0f, -0.02f), new Vector3(SlideMaxSize.x, SlideMaxSize.y, 1f));
            AudioSource speaker = CreateSpeaker(boardRoot.transform);
            VRCUnityVideoPlayer videoPlayer = CreateVideoPlayer(boardRoot, videoTexture, speaker);
            VRCAVProVideoPlayer avProPlayer = CreateAvProPlayer(boardRoot, boardScreens.AvPro, speaker);

            var board = UdonSharpUndo.AddComponent<PresentationBoard>(boardRoot);
            UdonBehaviour boardUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(board);
            boardUdon.SyncMethod = Networking.SyncType.Manual;

            var tabletRoot = new GameObject(TabletName);
            Undo.RegisterCreatedObjectUndo(tabletRoot, "Build presentation board");
            var personalTablet = UdonSharpUndo.AddComponent<PersonalTablet>(tabletRoot);
            UdonBehaviour tabletUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(personalTablet);
            tabletUdon.SyncMethod = Networking.SyncType.None;

            var tabletPanel = new GameObject("Panel");
            tabletPanel.transform.SetParent(tabletRoot.transform, false);
            CreateBackPlate(tabletPanel.transform, new Vector3(0f, 0.03f, 0.008f), new Vector3(0.45f, 0.57f, 0.012f), panelMaterial);
            ScreenSet tabletScreens = CreateScreenSet(tabletPanel.transform, new Vector3(0f, 0.2f, -0.004f), TabletScreenScale, videoMaterial, avProMaterial, slideMaterial, drawMaterial);
            GameObject tabletSurface = CreateDrawSurface(tabletScreens.Draw.transform.parent, new Vector3(0f, 0f, -0.12f), new Vector3(SlideMaxSize.x, SlideMaxSize.y, 1f));
            RectTransform tabletCanvas = CreateCanvas(tabletPanel.transform, new Vector3(0f, -0.115f, -0.002f), Quaternion.identity, TabletPanelScale, TabletPanelSize);
            PanelParts tabletParts = BuildControls(tabletCanvas, TabletPanelSize, boardUdon, false, tabletUdon, drawingUdon);

            Transform lectern = FindInScene(scene, "Kafedra");
            Vector3 lecternTop = (lectern != null ? lectern.position : LecternFallbackPosition) + Vector3.up * LecternHeight;
            float halfHeight = LecternPanelSize.y * LecternPanelScale * 0.5f;
            var panelRoot = new GameObject(PanelName);
            Undo.RegisterCreatedObjectUndo(panelRoot, "Build presentation board");
            panelRoot.transform.SetPositionAndRotation(
                lecternTop + Vector3.up * (halfHeight * Mathf.Cos(LecternPanelTilt * Mathf.Deg2Rad) + 0.015f),
                Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(LecternPanelTilt, 0f, 0f));
            CreateBackPlate(panelRoot.transform, new Vector3(0f, 0f, 0.009f),
                new Vector3(LecternPanelSize.x * LecternPanelScale + 0.02f, LecternPanelSize.y * LecternPanelScale + 0.02f, 0.015f), panelMaterial);
            RectTransform lecternCanvas = CreateCanvas(panelRoot.transform, Vector3.zero, Quaternion.identity, LecternPanelScale, LecternPanelSize);
            PanelParts lecternParts = BuildControls(lecternCanvas, LecternPanelSize, boardUdon, true, null, drawingUdon);

            var videoScreens = new List<GameObject> { boardScreens.Video, tabletScreens.Video };
            var avProScreens = new List<GameObject> { boardScreens.AvPro, tabletScreens.AvPro };
            var slideScreens = new List<Renderer> { boardScreens.Slide, tabletScreens.Slide };
            BuildSeatTablets(scene, personalTablet, videoMaterial, avProMaterial, slideMaterial, drawMaterial, panelMaterial,
                videoScreens, avProScreens, slideScreens);

            drawing.board = board;
            drawing.surfaces = new[] { boardSurface.transform, tabletSurface.transform };
            drawing.toolTexts = new[] { lecternParts.ToolText, tabletParts.ToolText };
            UdonSharpEditorUtility.CopyProxyToUdon(drawing);

            board.unityVideoPlayer = videoPlayer;
            board.avProVideoPlayer = avProPlayer;
            board.audioSources = new[] { speaker };
            board.videoScreens = videoScreens.ToArray();
            board.avProScreens = avProScreens.ToArray();
            board.slideScreens = slideScreens.ToArray();
            board.slides = LoadSlides();
            board.screenMaxSize = SlideMaxSize;
            board.urlInputs = new[] { lecternParts.Url, tabletParts.Url };
            board.volumeSliders = new[] { lecternParts.Volume, tabletParts.Volume };
            board.statusTexts = new[] { lecternParts.Status, tabletParts.Status };
            board.slideCounterTexts = new[] { lecternParts.Counter, tabletParts.Counter };
            board.pauseTexts = new[] { lecternParts.Pause, tabletParts.Pause };
            board.videoControls = new[] { lecternParts.VideoControls, tabletParts.VideoControls };
            board.slideControls = new[] { lecternParts.SlideControls, tabletParts.SlideControls };
            board.accessText = lecternParts.Access;
            board.requestText = lecternParts.Request;
            board.requestRow = lecternParts.RequestRow;
            board.revokeButton = lecternParts.RevokeButton;
            board.tabletControls = tabletParts.Controls;
            board.tabletHint = tabletParts.Hint;
            board.tabletRequestButton = tabletParts.RequestButton;
            board.tabletRequestText = tabletParts.Request;
            board.boardTabs = new[] { lecternParts.BoardTab, tabletParts.BoardTab };
            board.drawTabs = new[] { lecternParts.DrawTab, tabletParts.DrawTab };
            UdonSharpEditorUtility.CopyProxyToUdon(board);

            personalTablet.board = board;
            personalTablet.panel = tabletPanel;
            UdonSharpEditorUtility.CopyProxyToUdon(personalTablet);
            tabletPanel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = boardRoot;
            Debug.Log($"[PresentationBoard] Дошку зібрано: слайдів {board.slides.Length}, екранів {videoScreens.Count}");
        }

        [MenuItem("NextMetavers/Дошка/Оновити слайди з папки")]
        public static void RefreshSlides()
        {
            Texture2D[] slides = LoadSlides();
            int updated = 0;
            foreach (UdonBehaviour udon in Object.FindObjectsOfType<UdonBehaviour>(true))
            {
                if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(udon) != typeof(PresentationBoard)) continue;

                var board = (PresentationBoard)UdonSharpEditorUtility.GetProxyBehaviour(udon);
                Undo.RecordObject(udon, "Refresh slides");
                board.slides = slides;
                UdonSharpEditorUtility.CopyProxyToUdon(board);
                EditorUtility.SetDirty(udon);
                updated++;
            }

            if (updated == 0)
            {
                Debug.LogWarning("[PresentationBoard] У сцені немає дошки, спершу виконайте NextMetavers → Дошка → Зібрати дошку в сцені");
                return;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[PresentationBoard] Слайдів з папки {SlidesFolder}: {slides.Length}");
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                Build();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(UdonSharpProgramAsset.AnyUdonSharpScriptHasError() ? 1 : 0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        private static void RemoveOldObjects(Scene scene)
        {
            foreach (GameObject existing in scene.GetRootGameObjects())
            {
                if (existing.name == BoardName || existing.name == PanelName ||
                    existing.name == TabletName || existing.name == SeatsName || existing.name == DrawAreaName)
                {
                    Undo.DestroyObjectImmediate(existing);
                }
            }

            foreach (UdonBehaviour udon in Object.FindObjectsOfType<UdonBehaviour>(true))
            {
                if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(udon) != typeof(SeatStation)) continue;

                UdonSharpBehaviour proxy = UdonSharpEditorUtility.GetProxyBehaviour(udon);
                if (proxy != null) UdonSharpUndo.DestroyImmediate(proxy);
                else Undo.DestroyObjectImmediate(udon);
            }
        }

        private static void BuildSeatTablets(Scene scene, PersonalTablet personalTablet, Material videoMaterial,
            Material avProMaterial, Material slideMaterial, Material drawMaterial, Material bodyMaterial,
            List<GameObject> videoScreens, List<GameObject> avProScreens, List<Renderer> slideScreens)
        {
            var seatsRoot = new GameObject(SeatsName);
            Undo.RegisterCreatedObjectUndo(seatsRoot, "Build presentation board");

            VRCStation[] stations = Object.FindObjectsOfType<VRCStation>(true)
                .Where(station => station.gameObject.scene == scene)
                .OrderBy(station => station.transform.position.z)
                .ThenBy(station => station.transform.position.x)
                .ToArray();

            int index = 0;
            foreach (VRCStation station in stations)
            {
                index++;
                Transform seat = station.transform;
                Vector3 forward = seat.forward;
                Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);

                var tablet = new GameObject($"SeatTablet ({index})");
                tablet.transform.SetParent(seatsRoot.transform, false);
                tablet.transform.SetPositionAndRotation(
                    seat.position + forward * SeatTabletDistance + Vector3.up * SeatTabletHeight,
                    facing * Quaternion.Euler(SeatTabletTilt, 0f, 0f));

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(tablet.transform, false);
                body.transform.localScale = TabletBodySize;
                body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

                ScreenSet screens = CreateScreenSet(tablet.transform, new Vector3(0f, 0f, -0.009f), SeatScreenScale, videoMaterial, avProMaterial, slideMaterial, drawMaterial);
                videoScreens.Add(screens.Video);
                avProScreens.Add(screens.AvPro);
                slideScreens.Add(screens.Slide);

                var anchor = new GameObject($"SeatAnchor ({index})");
                anchor.transform.SetParent(seatsRoot.transform, false);
                anchor.transform.SetPositionAndRotation(
                    seat.position + forward * SeatAnchorDistance + Vector3.up * SeatAnchorHeight,
                    facing * Quaternion.Euler(SeatAnchorTilt, 0f, 0f));

                var seatTablet = UdonSharpUndo.AddComponent<SeatTablet>(body);
                UdonBehaviour seatUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(seatTablet);
                seatUdon.SyncMethod = Networking.SyncType.None;
                var seatUdonObject = new SerializedObject(seatUdon);
                seatUdonObject.FindProperty("interactText").stringValue = "Дошка зблизька";
                seatUdonObject.FindProperty("proximity").floatValue = 1.5f;
                seatUdonObject.ApplyModifiedPropertiesWithoutUndo();
                seatTablet.personalTablet = personalTablet;
                seatTablet.anchor = anchor.transform;
                seatTablet.tablet = tablet;
                UdonSharpEditorUtility.CopyProxyToUdon(seatTablet);

                var seatStation = UdonSharpUndo.AddComponent<SeatStation>(station.gameObject);
                UdonBehaviour stationUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(seatStation);
                stationUdon.SyncMethod = Networking.SyncType.None;
                seatStation.tablet = tablet;
                seatStation.personalTablet = personalTablet;
                seatStation.anchor = anchor.transform;
                UdonSharpEditorUtility.CopyProxyToUdon(seatStation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(stationUdon);

                tablet.SetActive(false);
            }

            Debug.Log($"[PresentationBoard] Планшетів на місцях: {index}");
        }

        private static ScreenSet CreateScreenSet(Transform parent, Vector3 localPosition, float scale,
            Material videoMaterial, Material avProMaterial, Material slideMaterial, Material drawMaterial)
        {
            var holder = new GameObject("Screens");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localScale = Vector3.one * scale;

            var set = new ScreenSet
            {
                Video = CreateScreen("VideoScreen", holder.transform, videoMaterial, ScreenSize),
                AvPro = CreateScreen("VideoScreenAVPro", holder.transform, avProMaterial, ScreenSize),
                Slide = CreateScreen("SlideScreen", holder.transform, slideMaterial, SlideMaxSize).GetComponent<Renderer>(),
                Draw = CreateScreen("DrawScreen", holder.transform, drawMaterial, SlideMaxSize),
            };
            set.Draw.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            set.Draw.SetActive(true);
            return set;
        }

        private static GameObject CreateDrawSurface(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            var surface = new GameObject("DrawSurface");
            surface.layer = SurfaceLayer;
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = localPosition;
            surface.transform.localScale = localScale;

            var collider = surface.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 0.02f);
            return surface;
        }

        private static BoardDrawing BuildDrawArea(RenderTexture target, Material strokeMaterial)
        {
            var areaRoot = new GameObject(DrawAreaName);
            Undo.RegisterCreatedObjectUndo(areaRoot, "Build presentation board");
            areaRoot.transform.position = DrawAreaPosition;
            areaRoot.layer = DrawLayer;

            var cameraObject = new GameObject("DrawCamera");
            cameraObject.layer = DrawLayer;
            cameraObject.transform.SetParent(areaRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -2f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = SlideMaxSize.y * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << DrawLayer;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 5f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            camera.targetTexture = target;
            camera.enabled = false;

            var strokes = new LineRenderer[StrokePoolSize];
            for (int i = 0; i < StrokePoolSize; i++)
            {
                var strokeObject = new GameObject($"Stroke ({i + 1})");
                strokeObject.layer = DrawLayer;
                strokeObject.transform.SetParent(areaRoot.transform, false);

                var line = strokeObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.alignment = LineAlignment.TransformZ;
                line.numCapVertices = 4;
                line.numCornerVertices = 4;
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                line.sharedMaterial = strokeMaterial;
                line.positionCount = 0;
                strokeObject.SetActive(false);
                strokes[i] = line;
            }

            var drawing = UdonSharpUndo.AddComponent<BoardDrawing>(areaRoot);
            UdonSharpEditorUtility.GetBackingUdonBehaviour(drawing).SyncMethod = Networking.SyncType.Manual;
            drawing.strokeLines = strokes;
            drawing.drawCamera = camera;
            drawing.palette = StrokePalette;
            drawing.widths = StrokeWidths;
            drawing.areaSize = SlideMaxSize;
            drawing.surfaceMask = 1 << SurfaceLayer;
            return drawing;
        }

        private static GameObject CreateScreen(string name, Transform parent, Material material, Vector2 size)
        {
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = name;
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(parent, false);
            screen.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            screen.SetActive(false);
            return screen;
        }

        private static void CreateBackPlate(Transform parent, Vector3 localPosition, Vector3 size, Material material)
        {
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Back";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(parent, false);
            back.transform.localPosition = localPosition;
            back.transform.localScale = size;
            back.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static AudioSource CreateSpeaker(Transform parent)
        {
            var speakerObject = new GameObject("Speaker");
            speakerObject.transform.SetParent(parent, false);
            speakerObject.transform.localPosition = new Vector3(0f, 0f, -0.1f);

            var audio = speakerObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0f;
            audio.volume = 0.8f;

            var spatial = new SerializedObject(speakerObject.AddComponent<VRCSpatialAudioSource>());
            spatial.FindProperty("Gain").floatValue = 14f;
            spatial.FindProperty("Near").floatValue = 16f;
            spatial.FindProperty("Far").floatValue = 60f;
            spatial.FindProperty("VolumetricRadius").floatValue = 5f;
            spatial.FindProperty("EnableSpatialization").boolValue = true;
            spatial.ApplyModifiedPropertiesWithoutUndo();
            return audio;
        }

        private static VRCAVProVideoPlayer CreateAvProPlayer(GameObject owner, GameObject screen, AudioSource speaker)
        {
            var player = owner.AddComponent<VRCAVProVideoPlayer>();
            var so = new SerializedObject(player);
            so.FindProperty("autoPlay").boolValue = false;
            so.FindProperty("loop").boolValue = false;
            so.FindProperty("EnableAutomaticResync").boolValue = false;
            so.FindProperty("maximumResolution").intValue = 1080;
            so.FindProperty("useLowLatency").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            var screenComponent = new SerializedObject(screen.AddComponent<VRCAVProVideoScreen>());
            screenComponent.FindProperty("videoPlayer").objectReferenceValue = player;
            screenComponent.FindProperty("materialIndex").intValue = 0;
            screenComponent.FindProperty("textureProperty").stringValue = "_MainTex";
            screenComponent.FindProperty("useSharedMaterial").boolValue = true;
            screenComponent.ApplyModifiedPropertiesWithoutUndo();

            var speakerComponent = new SerializedObject(speaker.gameObject.AddComponent<VRCAVProVideoSpeaker>());
            speakerComponent.FindProperty("videoPlayer").objectReferenceValue = player;
            speakerComponent.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        private static VRCUnityVideoPlayer CreateVideoPlayer(GameObject owner, RenderTexture target, AudioSource speaker)
        {
            var player = owner.AddComponent<VRCUnityVideoPlayer>();
            var so = new SerializedObject(player);
            SetEnum(so.FindProperty("renderMode"), "RenderTexture", 0);
            SetEnum(so.FindProperty("aspectRatio"), "FitInside", 3);
            so.FindProperty("targetTexture").objectReferenceValue = target;
            so.FindProperty("autoPlay").boolValue = false;
            so.FindProperty("loop").boolValue = false;
            so.FindProperty("EnableAutomaticResync").boolValue = false;
            so.FindProperty("maximumResolution").intValue = 1080;
            SerializedProperty audio = so.FindProperty("targetAudioSources");
            audio.arraySize = 1;
            audio.GetArrayElementAtIndex(0).objectReferenceValue = speaker;
            so.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        private static void SetEnum(SerializedProperty property, string valueName, int fallback)
        {
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                int index = Array.IndexOf(property.enumNames, valueName);
                if (index >= 0)
                {
                    property.enumValueIndex = index;
                    return;
                }
                Debug.LogWarning($"[PresentationBoard] {property.name}: немає значення {valueName}, є {string.Join(", ", property.enumNames)}");
            }
            property.intValue = fallback;
        }

        private static Transform FindInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static RectTransform CreateCanvas(Transform parent, Vector3 localPosition, Quaternion localRotation, float scale, Vector2 size)
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform));
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(parent, false);
            canvasRect.localPosition = localPosition;
            canvasRect.localRotation = localRotation;
            canvasRect.sizeDelta = size;
            canvasRect.localScale = Vector3.one * scale;

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<VRCUiShape>();

            Image background = CreateRect(canvasRect, "Background", 0, 0, size.x, size.y).gameObject.AddComponent<Image>();
            background.color = PanelColor;
            return canvasRect;
        }

        private static PanelParts BuildControls(RectTransform canvasRect, Vector2 size, UdonBehaviour boardUdon,
            bool isLectern, UdonBehaviour closeTarget, UdonBehaviour drawUdon)
        {
            var parts = new PanelParts();

            if (isLectern)
            {
                CreateText(CreateRect(canvasRect, "Title", 500, 16, 470, 52), "Головний стіл", 32, TextAnchor.MiddleRight, Color.white, FontStyle.Bold);
            }

            if (closeTarget != null)
            {
                CreateButton(CreateRect(canvasRect, "CloseButton", 700, 14, 270, 56), "Закрити", ButtonColor, closeTarget, "Hide");
            }

            RectTransform controls = CreateRect(canvasRect, "Controls", 0, 0, size.x, size.y);
            parts.Controls = controls.gameObject;
            string prefix = isLectern ? "" : "Tablet";

            CreateButton(CreateRect(controls, "BoardTabButton", 30, 14, 220, 52), "Дошка", ButtonColor, boardUdon, nameof(PresentationBoard.ShowBoardTab));
            CreateButton(CreateRect(controls, "DrawTabButton", 260, 14, 220, 52), "Малювання", ButtonColor, boardUdon, nameof(PresentationBoard.ShowDrawTab));

            RectTransform boardTab = CreateRect(controls, "BoardTab", 0, 0, size.x, size.y);
            parts.BoardTab = boardTab.gameObject;

            parts.Url = CreateUrlInput(CreateRect(boardTab, "UrlInput", 30, 80, 940, 70));

            CreateButton(CreateRect(boardTab, "PlayVideoButton", 30, 162, 460, 70), "Відео за посиланням", PrimaryColor, boardUdon, prefix + nameof(PresentationBoard.PlayVideoFromUrl));
            CreateButton(CreateRect(boardTab, "PlaySlidesButton", 510, 162, 460, 70), "Презентація за посиланням", PrimaryColor, boardUdon, prefix + nameof(PresentationBoard.PlaySlidesFromUrl));
            CreateButton(CreateRect(boardTab, "ProjectSlidesButton", 30, 244, 460, 70), "Слайди з проєкту", ButtonColor, boardUdon, prefix + nameof(PresentationBoard.ShowProjectSlides));
            CreateButton(CreateRect(boardTab, "TurnOffButton", 510, 244, 460, 70), "Вимкнути дошку", DangerColor, boardUdon, prefix + nameof(PresentationBoard.TurnOff));

            RectTransform videoControls = CreateRect(boardTab, "VideoControls", 30, 326, 940, 78);
            parts.Pause = CreateButton(CreateRect(videoControls, "PauseButton", 0, 0, 460, 78), "Пауза", ButtonColor, boardUdon, prefix + nameof(PresentationBoard.TogglePause));
            CreateButton(CreateRect(videoControls, "RestartButton", 480, 0, 460, 78), "З початку", ButtonColor, boardUdon, prefix + nameof(PresentationBoard.RestartVideo));
            parts.VideoControls = videoControls.gameObject;

            RectTransform slideControls = CreateRect(boardTab, "SlideControls", 30, 326, 940, 78);
            CreateButton(CreateRect(slideControls, "PrevButton", 0, 0, 300, 78), "◄  Назад", ButtonColor, boardUdon, prefix + nameof(PresentationBoard.PrevSlide));
            parts.Counter = CreateText(CreateRect(slideControls, "Counter", 300, 0, 340, 78), "Слайд –", 32, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            CreateButton(CreateRect(slideControls, "NextButton", 640, 0, 300, 78), "Вперед  ►", ButtonColor, boardUdon, prefix + nameof(PresentationBoard.NextSlide));
            parts.SlideControls = slideControls.gameObject;

            videoControls.gameObject.SetActive(false);
            slideControls.gameObject.SetActive(false);

            RectTransform drawTab = CreateRect(controls, "DrawTab", 0, 0, size.x, size.y);
            parts.DrawTab = drawTab.gameObject;

            CreateButton(CreateRect(drawTab, "ToolBrush", 30, 80, 300, 66), "Пензель", ButtonColor, drawUdon, nameof(BoardDrawing.ToolBrushMode));
            CreateButton(CreateRect(drawTab, "ToolLine", 350, 80, 300, 66), "Лінія", ButtonColor, drawUdon, nameof(BoardDrawing.ToolLineMode));
            CreateButton(CreateRect(drawTab, "ToolRect", 670, 80, 300, 66), "Прямокутник", ButtonColor, drawUdon, nameof(BoardDrawing.ToolRectMode));
            CreateButton(CreateRect(drawTab, "ToolEllipse", 30, 156, 300, 66), "Еліпс", ButtonColor, drawUdon, nameof(BoardDrawing.ToolEllipseMode));
            CreateButton(CreateRect(drawTab, "ToolArrow", 350, 156, 300, 66), "Стрілка", ButtonColor, drawUdon, nameof(BoardDrawing.ToolArrowMode));
            CreateButton(CreateRect(drawTab, "ToolEraser", 670, 156, 300, 66), "Гумка", ButtonColor, drawUdon, nameof(BoardDrawing.ToolEraserMode));

            CreateButton(CreateRect(drawTab, "WidthThin", 30, 232, 300, 66), "Тонко", ButtonColor, drawUdon, nameof(BoardDrawing.WidthThin));
            CreateButton(CreateRect(drawTab, "WidthMedium", 350, 232, 300, 66), "Середньо", ButtonColor, drawUdon, nameof(BoardDrawing.WidthMedium));
            CreateButton(CreateRect(drawTab, "WidthThick", 670, 232, 300, 66), "Товсто", ButtonColor, drawUdon, nameof(BoardDrawing.WidthThick));

            string[] colorEvents =
            {
                nameof(BoardDrawing.Color0), nameof(BoardDrawing.Color1), nameof(BoardDrawing.Color2),
                nameof(BoardDrawing.Color3), nameof(BoardDrawing.Color4), nameof(BoardDrawing.Color5),
            };
            for (int i = 0; i < colorEvents.Length; i++)
            {
                CreateButton(CreateRect(drawTab, $"Color ({i + 1})", 30 + i * 158, 308, 150, 66), "", StrokePalette[i], drawUdon, colorEvents[i]);
            }

            CreateButton(CreateRect(drawTab, "UndoButton", 30, 384, 460, 66), "Скасувати", ButtonColor, drawUdon, nameof(BoardDrawing.Undo));
            CreateButton(CreateRect(drawTab, "ClearButton", 510, 384, 460, 66), "Очистити все", DangerColor, drawUdon, nameof(BoardDrawing.ClearAll));

            parts.ToolText = CreateText(CreateRect(drawTab, "ToolText", 30, 456, 940, 44),
                "Пензель, середньо, колір 1", 26, TextAnchor.MiddleLeft, MutedTextColor, FontStyle.Normal);
            drawTab.gameObject.SetActive(false);

            RectTransform extras = isLectern ? canvasRect : controls;
            CreateText(CreateRect(extras, "VolumeLabel", 30, 524, 150, 44), "Гучність", 26, TextAnchor.MiddleLeft, MutedTextColor, FontStyle.Normal);
            parts.Volume = CreateSlider(CreateRect(extras, "VolumeSlider", 190, 532, 520, 28), boardUdon, nameof(PresentationBoard.VolumeChanged));
            CreateButton(CreateRect(extras, "ReloadButton", 730, 516, 240, 60), "Оновити відео", ButtonColor, boardUdon, nameof(PresentationBoard.ReloadVideo));

            if (isLectern)
            {
                CreateButton(CreateRect(canvasRect, "ClaimButton", 30, 590, 300, 70), "Стати ведучим", PrimaryColor, boardUdon, nameof(PresentationBoard.ClaimHost));
                parts.RevokeButton = CreateButton(CreateRect(canvasRect, "RevokeButton", 350, 590, 300, 70), "Забрати права", DangerColor, boardUdon, nameof(PresentationBoard.RevokeHost))
                    .transform.parent.gameObject;
                parts.Access = CreateButton(CreateRect(canvasRect, "AccessButton", 670, 590, 300, 70), "Доступ: лише ведучий", ButtonColor, boardUdon, nameof(PresentationBoard.ToggleAccess));

                RectTransform requestRow = CreateRect(canvasRect, "RequestRow", 30, 676, 940, 70);
                parts.Request = CreateText(CreateRect(requestRow, "RequestText", 0, 0, 450, 70), "Запит: немає", 26, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
                CreateButton(CreateRect(requestRow, "GrantButton", 460, 0, 230, 70), "Надати", PrimaryColor, boardUdon, nameof(PresentationBoard.GrantHost));
                CreateButton(CreateRect(requestRow, "DenyButton", 710, 0, 230, 70), "Відхилити", ButtonColor, boardUdon, nameof(PresentationBoard.DenyRequest));
                parts.RequestRow = requestRow.gameObject;
                requestRow.gameObject.SetActive(false);

                parts.Status = CreateText(CreateRect(canvasRect, "Status", 30, 766, 940, 100), "Дошку вимкнено", 26, TextAnchor.UpperLeft, MutedTextColor, FontStyle.Normal);
                return parts;
            }

            parts.Hint = CreateText(CreateRect(canvasRect, "ViewerHint", 30, 80, 940, 404),
                "Керує ведучий.\nЩоб отримати керування, надішліть запит на головний стіл.",
                30, TextAnchor.MiddleCenter, MutedTextColor, FontStyle.Normal).gameObject;
            parts.Request = CreateButton(CreateRect(canvasRect, "RequestButton", 30, 524, 940, 70), "Запросити право ведучого", PrimaryColor, boardUdon, nameof(PresentationBoard.RequestHost));
            parts.RequestButton = parts.Request.transform.parent.gameObject;
            parts.Status = CreateText(CreateRect(canvasRect, "Status", 30, 610, 940, 110), "Дошку вимкнено", 26, TextAnchor.UpperLeft, MutedTextColor, FontStyle.Normal);
            return parts;
        }

        private static RectTransform CreateRect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static RectTransform Stretch(RectTransform parent, string name, float padding)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, 0f);
            rect.offsetMax = new Vector2(-padding, 0f);
            return rect;
        }

        private static Text CreateText(RectTransform rect, string value, int size, TextAnchor alignment, Color color, FontStyle style)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = UiFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Text CreateButton(RectTransform rect, string label, Color color, UdonBehaviour udon, string eventName)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = color;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            UnityEventTools.AddStringPersistentListener(button.onClick, new UnityAction<string>(udon.SendCustomEvent), eventName);

            return CreateText(Stretch(rect, "Text", 8f), label, 28, TextAnchor.MiddleCenter, Color.white, FontStyle.Normal);
        }

        private static VRCUrlInputField CreateUrlInput(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            image.type = Image.Type.Sliced;

            var field = rect.gameObject.AddComponent<VRCUrlInputField>();
            Text placeholder = CreateText(Stretch(rect, "Placeholder", 18f), "Вставте посилання на відео (YouTube, mp4...)", 28, TextAnchor.MiddleLeft, new Color(0.45f, 0.45f, 0.5f), FontStyle.Italic);
            Text text = CreateText(Stretch(rect, "Text", 18f), "", 28, TextAnchor.MiddleLeft, new Color(0.1f, 0.1f, 0.12f), FontStyle.Normal);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var so = new SerializedObject(field);
            so.FindProperty("m_TargetGraphic").objectReferenceValue = image;
            so.FindProperty("m_TextComponent").objectReferenceValue = text;
            so.FindProperty("m_Placeholder").objectReferenceValue = placeholder;
            so.FindProperty("m_Navigation.m_Mode").intValue = (int)Navigation.Mode.None;
            so.ApplyModifiedPropertiesWithoutUndo();
            return field;
        }

        private static Slider CreateSlider(RectTransform rect, UdonBehaviour udon, string eventName)
        {
            var background = Stretch(rect, "Background", 0f).gameObject.AddComponent<Image>();
            background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            background.type = Image.Type.Sliced;
            background.color = ButtonColor;

            RectTransform fillArea = Stretch(rect, "Fill Area", 10f);
            RectTransform fill = Stretch(fillArea, "Fill", -10f);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = background.sprite;
            fillImage.type = Image.Type.Sliced;
            fillImage.color = PrimaryColor;

            RectTransform handleArea = Stretch(rect, "Handle Slide Area", 18f);
            RectTransform handle = Stretch(handleArea, "Handle", 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.anchoredPosition = Vector2.zero;
            handle.sizeDelta = new Vector2(40f, 12f);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.8f;
            UnityEventTools.AddStringPersistentListener(slider.onValueChanged, new UnityAction<string>(udon.SendCustomEvent), eventName);
            return slider;
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { "Materials", "Textures", "Slides" })
            {
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}")) AssetDatabase.CreateFolder(Root, folder);
            }
        }

        private static bool EnsureProgramAssets()
        {
            foreach (string script in new[] { "PresentationBoard", "PersonalTablet", "SeatTablet", "SeatStation", "BoardDrawing" })
            {
                EnsureProgramAsset(script);
            }

            UdonSharpCompilerV1.CompileSync();
            if (!UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) return true;

            Debug.LogError("[PresentationBoard] У U# скриптах є помилки компіляції, дошку не зібрано");
            return false;
        }

        private static void EnsureProgramAsset(string scriptName)
        {
            string path = $"{Root}/Scripts/{scriptName}.asset";
            var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>($"{Root}/Scripts/{scriptName}.cs");
                AssetDatabase.CreateAsset(programAsset, path);
            }

            if (programAsset.ScriptVersion < UdonSharpProgramVersion.CurrentVersion)
            {
                programAsset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.SaveAssets();
            }
        }

        private static RenderTexture GetVideoTexture()
        {
            const string path = Root + "/Textures/BoardVideo.renderTexture";
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (texture != null) return texture;

            texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Clamp,
            };
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        private static RenderTexture GetDrawTexture()
        {
            const string path = Root + "/Textures/BoardDraw.renderTexture";
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (texture != null) return texture;

            texture = new RenderTexture(2048, 1090, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                antiAliasing = 4,
                wrapMode = TextureWrapMode.Clamp,
            };
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        private static Material GetMaterial(string name, Shader shader, Texture texture, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.mainTexture = texture;
            if (material.HasProperty("_Color")) material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D[] LoadSlides()
        {
            if (!AssetDatabase.IsValidFolder(SlidesFolder)) return new Texture2D[0];

            return AssetDatabase.FindAssets("t:Texture2D", new[] { SlidesFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => NaturalSortKey(Path.GetFileNameWithoutExtension(path)), StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(texture => texture != null)
                .ToArray();
        }

        private static string NaturalSortKey(string name) => Regex.Replace(name, @"\d+", match => match.Value.PadLeft(8, '0'));

        private static void GenerateDemoSlides()
        {
            string[,] content =
            {
                {
                    "Дошка для презентацій",
                    "Це демонстраційні слайди з папки проєкту.\n\nГортайте їх кнопками «Назад» і «Вперед»\nна пульті біля кафедри або на планшеті біля крісла."
                },
                {
                    "Як додати свої слайди",
                    "1. Збережіть презентацію як зображення PNG\n     (PowerPoint: Файл → Експорт → PNG)\n2. Покладіть їх у папку Assets/PresentationBoard/Slides\n3. Меню NextMetavers → Дошка → Оновити слайди з папки\n4. Завантажте світ у VRChat заново"
                },
                {
                    "Відео та презентації за посиланням",
                    "Вставте посилання в поле на пульті й натисніть:\n\n«Відео за посиланням» — звичайне відео, синхронно в усіх\n«Презентація за посиланням» — відео, де кожен слайд\nтриває рівно 1 секунду (наприклад, PDF, перетворений на mp4)"
                },
            };

            int count = content.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                Texture2D texture = RenderSlide(content[i, 0], content[i, 1], $"{i + 1} / {count}");
                File.WriteAllBytes($"{SlidesFolder}/Demo{i + 1:00}.png", texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.Refresh();
        }

        private static Texture2D RenderSlide(string title, string body, string footer)
        {
            const int width = 1920;
            const int height = 1080;
            const int layer = 31;

            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var cameraObject = new GameObject("SlideCamera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.position = new Vector3(0f, -1000f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.97f, 0.97f, 0.98f);
            camera.cullingMask = 1 << layer;
            camera.targetTexture = target;

            var canvasObject = new GameObject("SlideCanvas", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 5f;
            var canvasRect = (RectTransform)canvasObject.transform;

            CreateRect(canvasRect, "Accent", 0, 0, width, 24).gameObject.AddComponent<Image>().color = PrimaryColor;
            CreateText(CreateRect(canvasRect, "Title", 140, 130, 1640, 140), title, 84, TextAnchor.MiddleLeft, new Color(0.1f, 0.12f, 0.18f), FontStyle.Bold);
            CreateRect(canvasRect, "Line", 140, 290, 220, 8).gameObject.AddComponent<Image>().color = PrimaryColor;
            CreateText(CreateRect(canvasRect, "Body", 140, 350, 1640, 560), body, 50, TextAnchor.UpperLeft, new Color(0.2f, 0.22f, 0.28f), FontStyle.Normal);
            CreateText(CreateRect(canvasRect, "Footer", 140, 960, 1640, 70), "NextMetavers  ·  " + footer, 34, TextAnchor.MiddleRight, new Color(0.5f, 0.52f, 0.58f), FontStyle.Normal);

            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            RenderTexture.ReleaseTemporary(target);
            return texture;
        }
    }

    internal class SlideTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PresentationBoardBuilder.SlidesFolder + "/") || !assetImporter.importSettingsMissing) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
