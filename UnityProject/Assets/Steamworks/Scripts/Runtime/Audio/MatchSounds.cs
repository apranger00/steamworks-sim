using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SteamworksSim.Game;
using SteamworksSim.Settings;
using UnityEngine;
using UnityEngine.Networking;

namespace SteamworksSim.Audio
{
    /// <summary>
    /// Field sound cues, loaded at startup from StreamingAssets/Sounds (mp3, wav or ogg). Which file plays
    /// for which cue is set in StreamingAssets/Sounds/sounds.json, so a sound pack can be swapped without code.
    /// A missing file falls back to a generated tone so the timing is still audible.
    /// </summary>
    public class MatchSounds : MonoBehaviour
    {
        [Serializable]
        class SoundMap
        {
            public string matchStart = "frc-start-sound.mp3";
            public string autoEnd = "frc-end.mp3";
            public string teleopStart = "frc-resume.mp3";
            public string endgame = "frc-warning.mp3";
            public string matchEnd = "frc-end.mp3";

            public string Get(MatchCue c)
            {
                switch (c)
                {
                    case MatchCue.MatchStart: return matchStart;
                    case MatchCue.AutoEnd: return autoEnd;
                    case MatchCue.TeleopStart: return teleopStart;
                    case MatchCue.Endgame: return endgame;
                    default: return matchEnd;
                }
            }
        }

        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Sounds");

        /// <summary>Opens the sounds folder in Explorer (creating it if needed).</summary>
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            Application.OpenURL(new Uri(Folder).AbsoluteUri);
        }

        public readonly List<string> problems = new List<string>();
        public bool Loaded { get; private set; }

        /// <summary>One line per cue: which file plays, or why a placeholder tone plays instead.</summary>
        public string StatusText { get; private set; } = "Loading match sounds...";

        static readonly string[] CueNames = { "Match start", "End of AUTO", "TELEOP start", "Endgame (0:30)", "Match end (0:00)" };

        /// <summary>The file in the sounds folder with this name (any letter case), or null.</summary>
        internal static string FindFile(string name)
        {
            string path = Path.Combine(Folder, name);
            if (File.Exists(path)) return path;
            if (!Directory.Exists(Folder)) return null;
            foreach (var f in Directory.GetFiles(Folder))
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        readonly Dictionary<MatchCue, AudioClip> clips = new Dictionary<MatchCue, AudioClip>();
        AudioSource source;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // field announcements are not positional
            source.priority = 0;
            ApplyVolume();
            GameSettings.Changed += ApplyVolume;
            StartCoroutine(LoadAll());
        }

        void OnDestroy() => GameSettings.Changed -= ApplyVolume;

        void ApplyVolume() { if (source != null) source.volume = GameSettings.MatchVolume; }

        MatchCue? pending; float pendingAt;

        public void Play(MatchCue cue)
        {
            if (!Loaded) { pending = cue; pendingAt = Time.unscaledTime; return; } // files still loading
            if (clips.TryGetValue(cue, out var clip) && clip != null) source.PlayOneShot(clip);
        }

        public void StopAll()
        {
            pending = null;
            if (source != null) source.Stop();
        }

        IEnumerator LoadAll()
        {
            var map = new SoundMap();
            string mapPath = Path.Combine(Folder, "sounds.json");
            if (File.Exists(mapPath))
            {
                try { JsonUtility.FromJsonOverwrite(File.ReadAllText(mapPath), map); }
                catch (Exception e) { problems.Add("sounds.json: " + e.Message); }
            }

            if (!Directory.Exists(Folder)) problems.Add("the sounds folder does not exist: " + Folder);
            var byFile = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            var status = new List<string>();
            foreach (MatchCue cue in Enum.GetValues(typeof(MatchCue)))
            {
                string file = map.Get(cue);
                AudioClip clip = null;
                if (!string.IsNullOrEmpty(file) && !byFile.TryGetValue(file, out clip))
                {
                    string path = FindFile(file);
                    if (path != null)
                    {
                        using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, TypeOf(path)))
                        {
                            yield return req.SendWebRequest();
                            if (req.result == UnityWebRequest.Result.Success)
                            {
                                clip = DownloadHandlerAudioClip.GetContent(req);
                                if (clip != null) clip.name = file;
                            }
                            else problems.Add($"{file}: {req.error}");
                        }
                    }
                    else problems.Add($"{file} not found in {Folder} (using a placeholder tone)");
                    byFile[file] = clip;
                }
                clips[cue] = clip != null ? clip : Placeholder(cue);
                status.Add(clip != null
                    ? $"{CueNames[(int)cue]}: {file} ({clip.length:0.0} s)"
                    : $"{CueNames[(int)cue]}: placeholder tone ({(string.IsNullOrEmpty(file) ? "no file set" : file + " not loaded")})");
            }
            StatusText = string.Join("\n", status);
            foreach (var p in problems) Debug.LogWarning("[Steamworks] Sounds: " + p);
            Debug.Log("[Steamworks] Match sounds from " + Folder + "\n" + StatusText);
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.audioMasterMute)
                Debug.LogWarning("[Steamworks] Unity's Game view audio is muted: click the speaker (Mute Audio) button on the Game view toolbar to hear sounds.");
#endif
            Loaded = true;
            if (pending.HasValue && Time.unscaledTime - pendingAt < 1.5f) Play(pending.Value);
            pending = null;
        }

        internal static AudioType TypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".aif": case ".aiff": return AudioType.AIFF;
                default: return AudioType.MPEG;
            }
        }

        // ---------------------------------------------------------------- placeholder tones
        static AudioClip Placeholder(MatchCue cue)
        {
            const int rate = 44100;
            switch (cue)
            {
                case MatchCue.MatchStart: // three rising notes
                    return Tone("start (placeholder)", rate, 0.75f, t =>
                    {
                        int n = Mathf.Min(2, (int)(t / 0.25f));
                        float f = new[] { 523f, 659f, 784f }[n], lt = t - n * 0.25f;
                        return Mathf.Sin(2f * Mathf.PI * f * t) * Env(lt, 0.22f) * 0.5f;
                    });
                case MatchCue.TeleopStart: // two-note chime
                    return Tone("resume (placeholder)", rate, 0.6f, t =>
                    {
                        float f = t < 0.2f ? 659f : 988f, lt = t < 0.2f ? t : t - 0.2f;
                        return Mathf.Sin(2f * Mathf.PI * f * t) * Env(lt, t < 0.2f ? 0.18f : 0.4f) * 0.5f;
                    });
                case MatchCue.Endgame: // whistle
                    return Tone("whistle (placeholder)", rate, 1.3f, t =>
                    {
                        float vib = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                        float s = Mathf.Sin(2f * Mathf.PI * 1050f * vib * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1580f * vib * t) * 0.3f;
                        return s * Env(t, 1.3f) * 0.45f;
                    });
                default: // buzzer
                    return Tone("buzzer (placeholder)", rate, 0.9f, t =>
                    {
                        float ph = (t * 180f) % 1f;
                        float saw = 2f * ph - 1f;
                        return Mathf.Clamp(saw * 1.6f, -1f, 1f) * Env(t, 0.9f) * 0.35f;
                    });
            }
        }

        static float Env(float t, float len) => Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((len - t) / 0.05f);

        static AudioClip Tone(string name, int rate, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)rate);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
