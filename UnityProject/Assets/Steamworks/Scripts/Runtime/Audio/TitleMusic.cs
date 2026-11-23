using System;
using System.Collections;
using System.IO;
using SteamworksSim.Settings;
using UnityEngine;
using UnityEngine.Networking;

namespace SteamworksSim.Audio
{
    /// <summary>
    /// Title-screen music: the file named by "music" in StreamingAssets/Sounds/sounds.json, looped while the
    /// menus are up (title, options, robot selection) and faded out when a match starts. Its volume is the
    /// "Music" option (times Master). No file, no music.
    /// </summary>
    public class TitleMusic : MonoBehaviour
    {
        [Serializable]
        class MusicMap { public string music = "title-music.mp3"; }

        const float FadeSeconds = 0.8f;

        AudioSource source;
        float fade;          // 0..1, eased toward Wanted
        public bool Wanted { get; set; }

        /// <summary>One line for the options screen: what plays, or why nothing does.</summary>
        public string StatusText { get; private set; } = "Loading music...";

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.volume = 0f;
            source.ignoreListenerPause = true; // menus may sit on top of a paused match
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            var map = new MusicMap();
            string mapPath = Path.Combine(MatchSounds.Folder, "sounds.json");
            if (File.Exists(mapPath))
            {
                try { JsonUtility.FromJsonOverwrite(File.ReadAllText(mapPath), map); }
                catch (Exception e) { Debug.LogWarning("[Steamworks] sounds.json: " + e.Message); }
            }
            if (string.IsNullOrEmpty(map.music)) { StatusText = "Music: none set in sounds.json"; yield break; }
            string path = MatchSounds.FindFile(map.music) ?? FindLikelyMusic();
            if (path == null) { StatusText = $"Music: {map.music} not found in {MatchSounds.Folder}"; Debug.LogWarning("[Steamworks] " + StatusText); yield break; }
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, MatchSounds.TypeOf(path)))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { StatusText = $"Music: {map.music}: {req.error}"; yield break; }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) { StatusText = $"Music: {map.music} could not be read"; yield break; }
                clip.name = Path.GetFileName(path);
                source.clip = clip;
                StatusText = $"Music: {clip.name} ({clip.length / 60f:0.0} min)";
                Debug.Log("[Steamworks] " + StatusText);
            }
        }

        // a file in the sounds folder that looks like the title track, if the one in sounds.json isn't there
        static string FindLikelyMusic()
        {
            if (!Directory.Exists(MatchSounds.Folder)) return null;
            foreach (var f in Directory.GetFiles(MatchSounds.Folder))
            {
                string n = Path.GetFileName(f).ToLowerInvariant(), ext = Path.GetExtension(n);
                if (ext != ".mp3" && ext != ".ogg" && ext != ".wav") continue;
                if (n.Contains("music") || n.Contains("steam_powered") || n.Contains("steam powered") || n.Contains("steam-powered")) return f;
            }
            return null;
        }

        bool playing;

        void Update()
        {
            if (source.clip == null) return;
            fade = Mathf.MoveTowards(fade, Wanted ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            source.volume = fade * Mathf.Clamp01(GameSettings.MusicVolume);
            // our own flag, not isPlaying: isPlaying can read false for a moment after Play(), which restarted
            // the track every frame (silence)
            if (fade > 0f && !playing) { source.Play(); playing = true; }          // back on the title screen: from the top
            else if (fade <= 0f && playing) { source.Stop(); playing = false; }
        }
    }
}
