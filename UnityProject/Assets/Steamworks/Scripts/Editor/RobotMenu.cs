using System.Text;
using SteamworksSim.Robots;
using UnityEditor;
using UnityEngine;

namespace SteamworksSim.EditorTools
{
    public static class RobotMenu
    {
        [MenuItem("Steamworks/Robots/List Robot Mods", priority = 40)]
        public static void ListRobots()
        {
            var res = RobotLibrary.LoadAll();
            var sb = new StringBuilder();
            sb.AppendLine($"[Steamworks] {res.robots.Count} robot(s). Built-in: {RobotLibrary.BuiltInDir}  Mods: {RobotLibrary.ModsDir}");
            foreach (var r in res.robots)
                sb.AppendLine($"  {(r.builtIn ? "built-in" : "mod     ")}  {r.id,-20} {r.name}  {r.LengthWithBumpers:0.#}x{r.WidthWithBumpers:0.#}x{r.frame.height:0.#} in, {r.mass:0} lb, {r.drivetrain.topSpeed} ft/s  [{r.hash}]");
            foreach (var p in res.problems) sb.AppendLine("  ! " + p);
            if (res.problems.Count > 0) Debug.LogWarning(sb.ToString()); else Debug.Log(sb.ToString());
        }

        [MenuItem("Steamworks/Robots/Open Mods Folder", priority = 41)]
        public static void OpenMods()
        {
            System.IO.Directory.CreateDirectory(RobotLibrary.ModsDir);
            EditorUtility.RevealInFinder(RobotLibrary.ModsDir);
        }
    }
}
