using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components;
using VRC.SDKBase;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace NextMetavers.EditorTools
{
    [InitializeOnLoad]
    internal static class EditorUrlResolver
    {
        private const float TimeoutSeconds = 60f;
        private const string HelpMessage = "Потрібні свіжий yt-dlp (NextMetavers → Дошка → Оновити yt-dlp) і встановлений Node.js або Deno.";
        private static readonly string[] DirectMediaExtensions = { ".mp4", ".webm", ".mov", ".m4v", ".mkv" };
        private static readonly List<Process> RunningProcesses = new List<Process>();

        private static string LocalYtDlpPath => Path.GetFullPath("Library/PresentationBoard/yt-dlp.exe");

        private static string VRChatYtDlpPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "VRChat", "VRChat", "Tools", "yt-dlp.exe");

        private static string YtDlpPath => File.Exists(LocalYtDlpPath) ? LocalYtDlpPath : VRChatYtDlpPath;

        static EditorUrlResolver()
        {
            VRCUnityVideoPlayer.StartResolveURLCoroutine = Resolve;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("NextMetavers/Дошка/Оновити yt-dlp")]
        private static void UpdateYtDlp()
        {
            if (!File.Exists(LocalYtDlpPath))
            {
                if (!File.Exists(VRChatYtDlpPath))
                {
                    Debug.LogError($"[PresentationBoard] yt-dlp не знайдено. Завантажте yt-dlp.exe з https://github.com/yt-dlp/yt-dlp/releases і покладіть у {LocalYtDlpPath}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(LocalYtDlpPath));
                File.Copy(VRChatYtDlpPath, LocalYtDlpPath);
            }

            try
            {
                EditorUtility.DisplayProgressBar("yt-dlp", "Оновлення yt-dlp...", 0.5f);
                Process process = StartProcess(LocalYtDlpPath, "-U");
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000)) process.Kill();
                Debug.Log($"[PresentationBoard] yt-dlp -U:\n{output.Result}{errors.Result}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Resolve(VRCUrl url, int resolution, Object videoPlayer, Action<string> onResolved, Action<VideoError> onError)
        {
            string source = url.Get();
            if (IsDirectMedia(source))
            {
                onResolved(source);
                return;
            }

            if (!File.Exists(YtDlpPath))
            {
                Debug.LogWarning($"[PresentationBoard] yt-dlp не знайдено, посилання передано плеєру без обробки. {HelpMessage}");
                onResolved(source);
                return;
            }

            ((MonoBehaviour)videoPlayer).StartCoroutine(ResolveRoutine(source, resolution, onResolved, onError));
        }

        private static IEnumerator ResolveRoutine(string source, int resolution, Action<string> onResolved, Action<VideoError> onError)
        {
            string format = $"best[height<=?{resolution}][ext=mp4][protocol^=http][vcodec!=none][acodec!=none]/best[height<=?{resolution}][protocol^=http][vcodec!=none][acodec!=none]";
            string arguments = "--no-playlist --no-warnings --check-formats --js-runtimes deno --js-runtimes node " +
                               "--extractor-args \"youtube:player_client=default,mweb,android_vr,web_safari\" " +
                               $"-f \"{format}\" --print \"%(format_id)s %(width)sx%(height)s\" --print urls \"{source.Replace("\"", "%22")}\"";

            Process process = StartProcess(YtDlpPath, arguments);
            RunningProcesses.Add(process);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!process.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            RunningProcesses.Remove(process);

            if (!process.HasExited)
            {
                process.Kill();
                Debug.LogError($"[PresentationBoard] yt-dlp не відповів за {TimeoutSeconds} с: {source}");
                onError(VideoError.Unknown);
                yield break;
            }

            while (!output.IsCompleted || !errors.IsCompleted) yield return null;

            string[] lines = output.Result.Split('\n').Select(line => line.Trim()).ToArray();
            string resolved = lines.FirstOrDefault(line => line.StartsWith("http"));
            string chosen = lines.FirstOrDefault(line => line.Length > 0 && !line.StartsWith("http"));
            if (!string.IsNullOrEmpty(chosen)) Debug.Log($"[PresentationBoard] yt-dlp формат: {chosen}");

            if (process.ExitCode != 0 || string.IsNullOrEmpty(resolved))
            {
                Debug.LogError($"[PresentationBoard] yt-dlp не зміг отримати відео {source}:\n{errors.Result}\n{HelpMessage}");
                onError(VideoError.Unknown);
                yield break;
            }

            onResolved(resolved);
        }

        private static bool IsDirectMedia(string source)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out Uri uri)) return false;
            return DirectMediaExtensions.Contains(Path.GetExtension(uri.AbsolutePath).ToLowerInvariant());
        }

        private static Process StartProcess(string fileName, string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
            };
            process.Start();
            return process;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;

            foreach (Process process in RunningProcesses)
            {
                if (!process.HasExited) process.Kill();
            }
            RunningProcesses.Clear();
        }
    }
}
