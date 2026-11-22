using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// Finds robot mods. Each robot is a folder containing robot.json (plus optional model.glb / icon.png):
    ///   built-in:  &lt;game&gt;_Data/StreamingAssets/Robots/&lt;id&gt;/   (Assets/StreamingAssets/Robots in the editor)
    ///   mods:      &lt;game folder&gt;/Mods/Robots/&lt;id&gt;/          (&lt;project folder&gt;/Mods/Robots in the editor)
    /// A mod with the same id as a built-in robot replaces it.
    /// </summary>
    public static class RobotLibrary
    {
        public class LoadResult
        {
            public readonly List<RobotDefinition> robots = new List<RobotDefinition>();
            public readonly List<string> problems = new List<string>(); // "folder: message"
        }

        public static string BuiltInDir => Path.Combine(Application.streamingAssetsPath, "Robots");
        public static string ModsDir => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Mods", "Robots");

        public static LoadResult LoadAll()
        {
            var res = new LoadResult();
            var byId = new Dictionary<string, RobotDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var (dir, builtIn) in new[] { (BuiltInDir, true), (ModsDir, false) })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var folder in Directory.GetDirectories(dir))
                {
                    var def = TryLoad(folder, builtIn, res.problems);
                    if (def != null) byId[def.id] = def; // mods override built-ins with the same id
                }
            }
            res.robots.AddRange(byId.Values);
            res.robots.Sort((a, b) => a.builtIn != b.builtIn ? (a.builtIn ? -1 : 1) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return res;
        }

        public static RobotDefinition TryLoad(string folder, bool builtIn, List<string> problems)
        {
            string file = Path.Combine(folder, "robot.json");
            string label = Path.GetFileName(folder);
            if (!File.Exists(file)) { problems.Add($"{label}: no robot.json"); return null; }
            try
            {
                string json = File.ReadAllText(file);
                var def = FromJson(json, label, problems);
                if (def == null) return null;
                def.folder = folder;
                def.builtIn = builtIn;
                return def;
            }
            catch (Exception e)
            {
                problems.Add($"{label}: {e.Message}");
                return null;
            }
        }

        /// <summary>Parses and validates a definition (also used for definitions received over the network).</summary>
        public static RobotDefinition FromJson(string json, string label, List<string> problems)
        {
            var def = JsonUtility.FromJson<RobotDefinition>(json);
            if (def == null) { problems.Add($"{label}: could not read robot.json"); return null; }
            if (string.IsNullOrWhiteSpace(def.id)) def.id = label;
            var errors = new List<string>(); var warnings = new List<string>();
            RobotRules.Validate(def, errors, warnings);
            foreach (var w in warnings) problems.Add($"{label}: warning: {w}");
            if (errors.Count > 0) { foreach (var e in errors) problems.Add($"{label}: {e}"); return null; }
            def.sourceJson = json;
            def.hash = Hash(JsonUtility.ToJson(def)); // canonical form, so whitespace/key order don't matter
            return def;
        }

        public static string Hash(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
