using UnityEngine;

namespace SteamworksSim
{
    /// <summary>
    /// Conversions between the field "spec" frame used by all data files and Unity.
    ///
    /// Spec frame (right-handed, inches): origin at field centre on the carpet, +X toward the BLUE
    /// alliance wall, +Y toward the scoring-table side, +Z up.
    /// Unity (left-handed, metres, Y up): x = X, y = Z, z = Y.
    ///
    /// Swapping Y and Z is a reflection, which is exactly what turns a right-handed frame into
    /// Unity's left-handed one. It also reverses triangle winding; MeshBuilder handles that.
    /// </summary>
    public static class Units
    {
        public const float Inch = 0.0254f;

        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x * Inch, z * Inch, y * Inch);
        public static Vector3 ToUnity(Vector3 spec) => ToUnity(spec.x, spec.y, spec.z);
        public static Vector3 ToSpec(Vector3 unity) => new Vector3(unity.x / Inch, unity.z / Inch, unity.y / Inch);

        /// <summary>Spec heading (degrees, counter-clockwise from +X toward +Y) to Unity yaw about +y.</summary>
        public static float YawFromSpec(float specDegrees) => -specDegrees;
        public static float SpecFromYaw(float unityYaw) => -unityYaw;

        public static Vector3 Spec(float[] a, int i = 0) =>
            new Vector3(a[i], a[i + 1], a.Length > i + 2 ? a[i + 2] : 0f);
    }
}
