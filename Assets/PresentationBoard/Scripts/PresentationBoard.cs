using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;
using VRC.SDKBase;

namespace NextMetavers
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PresentationBoard : UdonSharpBehaviour
    {
        private const int ModeOff = 0;
        private const int ModeVideo = 1;
        private const int ModeSlides = 2;
        private const int ModeVideoSlides = 3;

        private const float LoadCooldown = 5.5f;
        private const float MaxDrift = 1.5f;

        [Header("Відео")]
        public BaseVRCVideoPlayer unityVideoPlayer;
        public BaseVRCVideoPlayer avProVideoPlayer;
        public AudioSource[] audioSources;

        [Header("Екрани")]
        public GameObject[] videoScreens;
        public GameObject[] avProScreens;
        public Renderer[] slideScreens;
        public Texture2D[] slides;
        public Vector2 screenMaxSize = new Vector2(4.6f, 2.45f);

        [Header("Спільні елементи панелей")]
        public VRCUrlInputField[] urlInputs;
        public Slider[] volumeSliders;
        public Text[] statusTexts;
        public Text[] slideCounterTexts;
        public Text[] pauseTexts;
        public GameObject[] videoControls;
        public GameObject[] slideControls;

        [Header("Головний стіл")]
        public Text accessText;
        public Text requestText;
        public GameObject requestRow;
        public GameObject revokeButton;

        [Header("Вкладки")]
        public GameObject[] boardTabs;
        public GameObject[] drawTabs;

        [Header("Планшет")]
        public GameObject tabletControls;
        public GameObject tabletHint;
        public GameObject tabletRequestButton;
        public Text tabletRequestText;

        [UdonSynced] private int _mode;
        [UdonSynced] private VRCUrl _url = VRCUrl.Empty;
        [UdonSynced] private int _urlVersion;
        [UdonSynced] private bool _paused;
        [UdonSynced] private float _pausedTime;
        [UdonSynced] private double _startServerTime;
        [UdonSynced] private int _slide;
        [UdonSynced] private int _hostId = -1;
        [UdonSynced] private int _requestId = -1;
        [UdonSynced] private bool _openAccess;

        private int _loadedUrlVersion;
        private bool _videoReady;
        private bool _ownerStartPending;
        private bool _retryScheduled;
        private int _videoSlideCount;
        private float _lastLoadTime = -100f;
        private float _lastSeekTime = -100f;
        private float _volume = 0.8f;
        private string _status = "";
        private BaseVRCVideoPlayer _player;
        private GameObject[] _playerScreens;

        private void Start()
        {
            EnsurePlayer();
            if (volumeSliders.Length > 0) _volume = volumeSliders[0].value;
            ApplyVolume();
            ApplyState();
            SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
        }

        public override void OnDeserialization()
        {
            ApplyState();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            UpdateUi();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && Networking.IsMaster)
            {
                bool changed = false;
                if (player.playerId == _hostId)
                {
                    _hostId = -1;
                    changed = true;
                }
                if (player.playerId == _requestId)
                {
                    _requestId = -1;
                    changed = true;
                }

                if (changed)
                {
                    TakeOwnership();
                    RequestSerialization();
                }
            }
            UpdateUi();
        }

        public void RefreshPanels()
        {
            UpdateUi();
        }

        public void ShowBoardTab()
        {
            SwitchTab(true);
        }

        public void ShowDrawTab()
        {
            SwitchTab(false);
        }

        private void SwitchTab(bool boardTab)
        {
            foreach (GameObject tab in boardTabs)
            {
                if (tab != null) tab.SetActive(boardTab);
            }

            foreach (GameObject tab in drawTabs)
            {
                if (tab != null) tab.SetActive(!boardTab);
            }
        }

        public void PlayVideoFromUrl()
        {
            StartUrl(ModeVideo);
        }

        public void PlaySlidesFromUrl()
        {
            StartUrl(ModeVideoSlides);
        }

        public void ShowProjectSlides()
        {
            if (slides == null || slides.Length == 0)
            {
                SetStatus("У проєкт не додано слайдів");
                return;
            }

            TakeOwnership();
            _mode = ModeSlides;
            _slide = 0;
            Sync();
        }

        public void TurnOff()
        {
            TakeOwnership();
            _mode = ModeOff;
            Sync();
        }

        public void NextSlide()
        {
            if (_mode != ModeSlides && _mode != ModeVideoSlides) return;
            if (_slide >= GetSlideCount() - 1) return;

            TakeOwnership();
            _slide++;
            Sync();
        }

        public void PrevSlide()
        {
            if (_mode != ModeSlides && _mode != ModeVideoSlides) return;
            if (_slide <= 0) return;

            TakeOwnership();
            _slide--;
            Sync();
        }

        public void TogglePause()
        {
            if (_mode != ModeVideo || !_videoReady) return;

            TakeOwnership();
            if (_paused)
            {
                if (_pausedTime >= _player.GetDuration() - 0.5f) _pausedTime = 0f;
                _startServerTime = Networking.GetServerTimeInSeconds() - _pausedTime;
                _paused = false;
            }
            else
            {
                _pausedTime = _player.GetTime();
                _paused = true;
            }
            Sync();
        }

        public void RestartVideo()
        {
            if (_mode != ModeVideo) return;

            TakeOwnership();
            _paused = false;
            _pausedTime = 0f;
            _startServerTime = Networking.GetServerTimeInSeconds();
            Sync();
        }

        public void TabletPlayVideoFromUrl()
        {
            if (!HasTabletRights()) return;
            PlayVideoFromUrl();
        }

        public void TabletPlaySlidesFromUrl()
        {
            if (!HasTabletRights()) return;
            PlaySlidesFromUrl();
        }

        public void TabletShowProjectSlides()
        {
            if (!HasTabletRights()) return;
            ShowProjectSlides();
        }

        public void TabletTurnOff()
        {
            if (!HasTabletRights()) return;
            TurnOff();
        }

        public void TabletNextSlide()
        {
            if (!HasTabletRights()) return;
            NextSlide();
        }

        public void TabletPrevSlide()
        {
            if (!HasTabletRights()) return;
            PrevSlide();
        }

        public void TabletTogglePause()
        {
            if (!HasTabletRights()) return;
            TogglePause();
        }

        public void TabletRestartVideo()
        {
            if (!HasTabletRights()) return;
            RestartVideo();
        }

        public void ReloadVideo()
        {
            if (_mode != ModeVideo && _mode != ModeVideoSlides) return;

            _loadedUrlVersion = -1;
            ApplyState();
        }

        public void ClaimHost()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            TakeOwnership();
            _hostId = local.playerId;
            if (_requestId == local.playerId) _requestId = -1;
            Sync();
        }

        public bool HasDrawRights()
        {
            return _openAccess || IsHost();
        }

        public void RequestHost()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            if (local.playerId == _hostId)
            {
                SetStatus("Ви вже ведучий");
                return;
            }

            if (_requestId == local.playerId)
            {
                SetStatus("Запит уже надіслано, чекайте відповіді");
                return;
            }

            if (_requestId != -1 && Utilities.IsValid(VRCPlayerApi.GetPlayerById(_requestId)))
            {
                SetStatus("Зачекайте: є інший запит");
                return;
            }

            TakeOwnership();
            _requestId = local.playerId;
            Sync();
        }

        public void GrantHost()
        {
            if (_requestId == -1) return;

            TakeOwnership();
            _hostId = _requestId;
            _requestId = -1;
            Sync();
        }

        public void DenyRequest()
        {
            TakeOwnership();
            _requestId = -1;
            Sync();
        }

        public void RevokeHost()
        {
            TakeOwnership();
            _hostId = -1;
            Sync();
        }

        public void ToggleAccess()
        {
            TakeOwnership();
            _openAccess = !_openAccess;
            Sync();
        }

        public void VolumeChanged()
        {
            foreach (Slider slider in volumeSliders)
            {
                if (slider != null && Mathf.Abs(slider.value - _volume) > 0.005f)
                {
                    _volume = slider.value;
                    break;
                }
            }

            foreach (Slider slider in volumeSliders)
            {
                if (slider != null && Mathf.Abs(slider.value - _volume) > 0.005f) slider.value = _volume;
            }

            ApplyVolume();
        }

        public override void OnVideoReady()
        {
            _videoReady = true;
            _videoSlideCount = Mathf.Max(1, Mathf.FloorToInt(_player.GetDuration()));

            if (_ownerStartPending && Networking.IsOwner(gameObject))
            {
                _ownerStartPending = false;
                _startServerTime = Networking.GetServerTimeInSeconds();
                RequestSerialization();
            }

            ApplyVideoState();
            UpdateUi();
        }

        public override void OnVideoEnd()
        {
            if (_mode != ModeVideo) return;

            if (Networking.IsOwner(gameObject))
            {
                _paused = true;
                _pausedTime = _player.GetDuration();
                RequestSerialization();
            }
            SetStatus("Відео закінчилося");
        }

        public override void OnVideoError(VideoError videoError)
        {
            _videoReady = false;
            ShowVideoScreens(false);

            if (videoError == VideoError.RateLimited)
            {
                SetStatus("Занадто часте завантаження, повтор через 5 секунд...");
                ScheduleRetry(LoadCooldown);
                return;
            }

            string reason = "невідома помилка";
            if (videoError == VideoError.InvalidURL) reason = "неправильне посилання";
            else if (videoError == VideoError.AccessDenied) reason = "посилання заблоковано (увімкніть Allow Untrusted URLs у налаштуваннях VRChat)";
            else if (videoError == VideoError.PlayerError) reason = "не вдалося відтворити файл";
            SetStatus("Помилка: " + reason);
        }

        public void _Tick()
        {
            SendCustomEventDelayedSeconds(nameof(_Tick), 1f);

            if (_mode == ModeVideo && _videoReady && !_paused && !IsLive() && Time.realtimeSinceStartup - _lastSeekTime > 3f)
            {
                float expected = GetExpectedTime();
                float actual = _player.GetTime();
                if (Mathf.Abs(actual - expected) > MaxDrift)
                {
                    if (Networking.IsOwner(gameObject))
                    {
                        _startServerTime = Networking.GetServerTimeInSeconds() - actual;
                        RequestSerialization();
                    }
                    else if (expected < _player.GetDuration())
                    {
                        SeekTo(expected);
                    }
                }
            }
            UpdateUi();
        }

        public void _RetryLoad()
        {
            _retryScheduled = false;
            if (_mode != ModeVideo && _mode != ModeVideoSlides) return;
            if (_videoReady) return;

            _loadedUrlVersion = -1;
            ApplyState();
        }

        public void _PauseAfterSlideSeek()
        {
            if (_mode == ModeVideoSlides) _player.Pause();
        }

        private bool IsHost()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) && local.playerId == _hostId;
        }

        private bool HasTabletRights()
        {
            if (_openAccess || IsHost()) return true;

            SetStatus("Керує ведучий. Надішліть запит на головний стіл");
            return false;
        }

        private void StartUrl(int mode)
        {
            VRCUrl url = VRCUrl.Empty;
            foreach (VRCUrlInputField input in urlInputs)
            {
                if (input == null) continue;
                VRCUrl entered = input.GetUrl();
                if (entered != null && !string.IsNullOrEmpty(entered.Get())) url = entered;
            }

            bool hasNewUrl = !string.IsNullOrEmpty(url.Get());
            if (!hasNewUrl && (_url == null || string.IsNullOrEmpty(_url.Get())))
            {
                SetStatus("Спершу вставте посилання в поле вище");
                return;
            }

            TakeOwnership();
            if (hasNewUrl) _url = url;
            _urlVersion++;
            _mode = mode;
            _slide = 0;
            _paused = mode == ModeVideoSlides;
            _pausedTime = 0f;
            _startServerTime = Networking.GetServerTimeInSeconds();
            _ownerStartPending = mode == ModeVideo;

            foreach (VRCUrlInputField input in urlInputs)
            {
                if (input != null) input.SetUrl(VRCUrl.Empty);
            }
            Sync();
        }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        private void Sync()
        {
            RequestSerialization();
            ApplyState();
        }

        private void ApplyState()
        {
            bool usesVideo = _mode == ModeVideo || _mode == ModeVideoSlides;

            if (usesVideo)
            {
                if (_loadedUrlVersion != _urlVersion) LoadCurrentUrl();
                else ApplyVideoState();
            }
            else if (_loadedUrlVersion != 0)
            {
                _player.Stop();
                _videoReady = false;
                _loadedUrlVersion = 0;
            }

            ShowVideoScreens(usesVideo && _videoReady);
            ShowProjectSlide();

            if (_mode == ModeOff) SetStatus("Дошку вимкнено");
            else if (_mode == ModeSlides) SetStatus("Слайди з проєкту");
            UpdateUi();
        }

        private void LoadCurrentUrl()
        {
            _videoReady = false;

            float wait = _lastLoadTime + LoadCooldown - Time.realtimeSinceStartup;
            if (wait > 0f)
            {
                SetStatus("Завантаження за кілька секунд...");
                ScheduleRetry(wait);
                return;
            }

            _loadedUrlVersion = _urlVersion;
            _lastLoadTime = Time.realtimeSinceStartup;
            _player.Stop();
            _player.LoadURL(_url);
            SetStatus("Завантаження...");
        }

        private void ScheduleRetry(float delay)
        {
            if (_retryScheduled) return;

            _retryScheduled = true;
            SendCustomEventDelayedSeconds(nameof(_RetryLoad), delay);
        }

        private void ApplyVideoState()
        {
            if (!_videoReady) return;

            ShowVideoScreens(true);

            if (_mode == ModeVideoSlides)
            {
                SeekTo(Mathf.Clamp(_slide, 0, _videoSlideCount - 1) + 0.5f);
                _player.Play();
                SendCustomEventDelayedSeconds(nameof(_PauseAfterSlideSeek), 0.2f);
                SetStatus("Презентація за посиланням");
                return;
            }

            if (_paused)
            {
                _player.Pause();
                float duration = _player.GetDuration();
                if (!IsLive()) SeekTo(Mathf.Min(_pausedTime, duration - 0.1f));
                SetStatus(_pausedTime >= duration - 0.5f ? "Відео закінчилося" : "Пауза");
            }
            else
            {
                if (!_player.IsPlaying) _player.Play();
                float expected = GetExpectedTime();
                if (!IsLive() && Mathf.Abs(_player.GetTime() - expected) > 0.5f) SeekTo(expected);
                SetStatus("Відтворення");
            }
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;

#if UNITY_ANDROID
            _player = avProVideoPlayer;
            _playerScreens = avProScreens;
            HideScreens(videoScreens);
#else
            _player = unityVideoPlayer;
            _playerScreens = videoScreens;
            HideScreens(avProScreens);
#endif
        }

        private void HideScreens(GameObject[] screens)
        {
            foreach (GameObject screen in screens)
            {
                if (screen != null) screen.SetActive(false);
            }
        }

        private void ShowVideoScreens(bool show)
        {
            EnsurePlayer();
            foreach (GameObject screen in _playerScreens)
            {
                if (screen != null) screen.SetActive(show);
            }
        }

        private void ShowProjectSlide()
        {
            bool show = _mode == ModeSlides && slides != null && slides.Length > 0;
            Texture2D texture = null;
            float width = 0f;
            float height = 0f;

            if (show)
            {
                texture = slides[Mathf.Clamp(_slide, 0, slides.Length - 1)];
                show = texture != null;
            }

            if (show)
            {
                float aspect = (float)texture.width / texture.height;
                width = screenMaxSize.x;
                height = width / aspect;
                if (height > screenMaxSize.y)
                {
                    height = screenMaxSize.y;
                    width = height * aspect;
                }
            }

            foreach (Renderer screen in slideScreens)
            {
                if (screen == null) continue;

                screen.gameObject.SetActive(show);
                if (!show) continue;

                screen.material.SetTexture("_MainTex", texture);
                screen.transform.localScale = new Vector3(width, height, 1f);
            }
        }

        private void ApplyVolume()
        {
            foreach (AudioSource source in audioSources)
            {
                if (source != null) source.volume = _volume;
            }
        }

        private void SeekTo(float time)
        {
            _player.SetTime(Mathf.Max(0f, time));
            _lastSeekTime = Time.realtimeSinceStartup;
        }

        private float GetExpectedTime()
        {
            return (float)(Networking.GetServerTimeInSeconds() - _startServerTime);
        }

        private bool IsLive()
        {
            float duration = _player.GetDuration();
            return duration <= 0.01f || duration >= float.MaxValue;
        }

        private int GetSlideCount()
        {
            if (_mode == ModeSlides) return slides == null ? 0 : slides.Length;
            if (_mode == ModeVideoSlides) return _videoReady ? _videoSlideCount : 0;
            return 0;
        }

        private string GetPlayerName(int playerId)
        {
            if (playerId == -1) return "немає";

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
            return Utilities.IsValid(player) ? player.displayName : "гравець вийшов";
        }

        private void SetStatus(string status)
        {
            _status = status;
            UpdateUi();
        }

        private void UpdateUi()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            bool isHost = IsHost();
            bool tabletHasControl = isHost || _openAccess;

            if (tabletControls != null) tabletControls.SetActive(tabletHasControl);
            if (tabletHint != null) tabletHint.SetActive(!tabletHasControl);
            if (tabletRequestButton != null) tabletRequestButton.SetActive(!tabletHasControl);
            if (tabletRequestText != null)
            {
                bool sent = Utilities.IsValid(local) && _requestId == local.playerId;
                tabletRequestText.text = sent ? "Запит надіслано, чекайте" : "Запросити право ведучого";
            }

            if (requestRow != null) requestRow.SetActive(_requestId != -1);
            if (requestText != null) requestText.text = "Запит: " + GetPlayerName(_requestId);
            if (revokeButton != null) revokeButton.SetActive(_hostId != -1);
            if (accessText != null) accessText.text = _openAccess ? "Доступ: усім" : "Доступ: лише ведучий";

            foreach (GameObject controls in videoControls)
            {
                if (controls != null) controls.SetActive(_mode == ModeVideo);
            }

            foreach (GameObject controls in slideControls)
            {
                if (controls != null) controls.SetActive(_mode == ModeSlides || _mode == ModeVideoSlides);
            }

            foreach (Text text in pauseTexts)
            {
                if (text != null) text.text = _paused ? "Грати" : "Пауза";
            }

            int count = GetSlideCount();
            string counter = count > 0 ? "Слайд " + (Mathf.Min(_slide, count - 1) + 1) + " / " + count : "Слайд –";
            foreach (Text text in slideCounterTexts)
            {
                if (text != null) text.text = counter;
            }

            string line = _status;
            if (_mode == ModeVideo && _videoReady && !IsLive())
            {
                float time = _paused ? _pausedTime : _player.GetTime();
                line += "   " + FormatTime(time) + " / " + FormatTime(_player.GetDuration());
            }
            line += "\nВедучий: " + GetPlayerName(_hostId);
            if (_openAccess) line += ", доступ у всіх";

            foreach (Text text in statusTexts)
            {
                if (text != null) text.text = line;
            }
        }

        private string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
