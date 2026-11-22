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
            public string endgame = "frc-warning.mp3";
            public string matchEnd = "frc-end.mp3";

            public string Get(MatchCue c)
            {
                switch (c)
                {
                    case MatchCue.MatchStart: return matchStart;
                    case MatchCue.AutoEnd: return autoEnd;
                    case MatchCue.Endgame: return endgame;
                    default: return matchEnd;
                }
            }
        }

        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Sounds");

        public readonly List<string> problems = new List<string>();
        public bool Loaded { get; private set; }

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

            var byFile = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            foreach (MatchCue cue in Enum.GetValues(typeof(MatchCue)))
            {
                string file = map.Get(cue);
                AudioClip clip = null;
                if (!string.IsNullOrEmpty(file) && !byFile.TryGetValue(file, out clip))
                {
                    string path = Path.Combine(Folder, file);
                    if (File.Exists(path))
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
            }
            foreach (var p in problems) Debug.LogWarning("[Steamworks] Sounds: " + p);
            Loaded = true;
            if (pending.HasValue && Time.unscaledTime - pendingAt < 1.5f) Play(pending.Value);
            pending = null;
        }

        static AudioType TypeOf(string path)
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
