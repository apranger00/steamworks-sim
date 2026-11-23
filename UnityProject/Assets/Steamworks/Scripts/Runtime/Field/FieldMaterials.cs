using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteamworksSim.Field
{
    /// <summary>
    /// Material palette keyed by the names used in field.json (same colours as the browser prototype).
    /// Built for URP Lit; falls back to the Standard shader if URP is not the active pipeline.
    /// </summary>
    public class FieldMaterials
    {
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        readonly Shader lit;
        readonly bool urp;

        struct Spec
        {
            public Color color; public float metal, smooth; public bool transparent, doubleSided; public Color emission;
        }

        static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        static readonly Dictionary<string, Spec> Palette = new Dictionary<string, Spec>
        {
            ["carpet"] = new Spec { color = Hex(0x387d2d), smooth = 0.05f },
            ["poly"] = new Spec { color = Hex(0xcfe6ff, 0.22f), smooth = 0.9f, transparent = true, doubleSided = true },
            ["polyHopper"] = new Spec { color = Hex(0xdfeeff, 0.18f), smooth = 0.9f, transparent = true, doubleSided = true },
            ["alu"] = new Spec { color = Hex(0xb9bec4), metal = 0.6f, smooth = 0.6f },
            ["steel"] = new Spec { color = Hex(0x6e747a), metal = 0.5f, smooth = 0.5f },
            ["steelDark"] = new Spec { color = Hex(0x3b3f44), metal = 0.3f, smooth = 0.3f },
            ["deck"] = new Spec { color = Hex(0x8a8f94), smooth = 0.1f },
            ["hole"] = new Spec { color = Hex(0x0b0b0d), smooth = 0f },
            ["red"] = new Spec { color = Hex(0xd32030), smooth = 0.3f },
            ["blue"] = new Spec { color = Hex(0x1f5fd1), smooth = 0.3f },
            ["redDark"] = new Spec { color = Hex(0x7a2a24), metal = 0.25f, smooth = 0.4f },
            ["blueDark"] = new Spec { color = Hex(0x26395f), metal = 0.25f, smooth = 0.4f },
            ["copper"] = new Spec { color = Hex(0xb06a3b), metal = 0.6f, smooth = 0.65f, doubleSided = true },
            ["retro"] = new Spec { color = Hex(0xe8eef2), metal = 0.2f, smooth = 0.8f, emission = Hex(0x2a2f33) },
            ["tank"] = new Spec { color = Hex(0x9a7b4f), metal = 0.5f, smooth = 0.55f },
            ["rotor"] = new Spec { color = Hex(0xc9a227), metal = 0.6f, smooth = 0.65f, doubleSided = true },
            ["spring"] = new Spec { color = Hex(0x9aa3ab), metal = 0.8f, smooth = 0.7f },
            ["hdpe"] = new Spec { color = Hex(0xf1efe6), smooth = 0.4f },
            ["rope"] = new Spec { color = Hex(0xc8a86b), smooth = 0f },
            ["touchpad"] = new Spec { color = Hex(0xffffff, 0.55f), smooth = 0.9f, transparent = true, doubleSided = true },
            ["dome"] = new Spec { color = Hex(0xf5f5f5, 0.8f), smooth = 0.8f, transparent = true, doubleSided = true },
            ["net"] = new Spec { color = Hex(0x222222, 0.25f), smooth = 0f, transparent = true, doubleSided = true },
            ["pushPanel"] = new Spec { color = Hex(0xffe27a, 0.45f), smooth = 0.9f, transparent = true, doubleSided = true },
            ["bin"] = new Spec { color = Hex(0x2b2b2b), smooth = 0.1f },
            ["fuel"] = new Spec { color = Hex(0xf3e21b), smooth = 0.45f },
            ["gear"] = new Spec { color = Hex(0xd9a514), metal = 0.15f, smooth = 0.55f },
            ["wheelGreen"] = new Spec { color = Hex(0x6cc24a), smooth = 0.25f },
            ["belt"] = new Spec { color = Hex(0x141414), smooth = 0.15f },
            ["motor"] = new Spec { color = Hex(0x1b1c1f), metal = 0.3f, smooth = 0.5f },
            ["motorCap"] = new Spec { color = Hex(0xe0742c), metal = 0.2f, smooth = 0.5f },
            ["plate"] = new Spec { color = Hex(0x8e959c), metal = 0.55f, smooth = 0.45f },
            ["tape_charcoal"] = new Spec { color = Hex(0x36393d), smooth = 0.1f },
            ["tape_white"] = new Spec { color = Hex(0xf4f4f4), smooth = 0.1f },
            ["tape_green"] = new Spec { color = Hex(0x1fbf4c), smooth = 0.1f },
            ["tape_red"] = new Spec { color = Hex(0xe0202e), smooth = 0.1f },
            ["tape_blue"] = new Spec { color = Hex(0x2266e0), smooth = 0.1f },
            ["cadGhost"] = new Spec { color = Hex(0x7fd0ff, 0.35f), smooth = 0.4f, transparent = true, doubleSided = true },
        };

        /// <summary>The lit shader every field material uses (URP Lit, or Standard outside URP).</summary>
        public Shader LitShader => lit;

        public FieldMaterials(Shader litShader = null)
        {
            lit = litShader != null ? litShader : Shader.Find("Universal Render Pipeline/Lit");
            urp = lit != null;
            if (!urp) lit = Shader.Find("Standard");
        }

        public Material Get(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "steel";
            if (cache.TryGetValue(key, out var m)) return m;
            if (!Palette.TryGetValue(key, out var s))
            {
                Debug.LogWarning($"[Steamworks] Unknown material '{key}', using steel.");
                s = Palette["steel"];
            }
            m = Create(key, s);
            cache[key] = m;
            return m;
        }

        Material Create(string key, Spec s)
        {
            var m = new Material(lit) { name = "Field_" + key };
            if (urp)
            {
                m.SetColor("_BaseColor", s.color);
                m.SetFloat("_Metallic", s.metal);
                m.SetFloat("_Smoothness", s.smooth);
                if (s.transparent)
                {
                    m.SetFloat("_Surface", 1f); // Transparent
                    m.SetFloat("_Blend", 0f);   // Alpha
                    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    m.SetFloat("_ZWrite", 0f);
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = (int)RenderQueue.Transparent;
                }
                if (s.doubleSided) m.SetFloat("_Cull", (float)CullMode.Off);
                if (s.emission.maxColorComponent > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", s.emission);
                }
            }
            else
            {
                m.color = s.color;
                m.SetFloat("_Metallic", s.metal);
                m.SetFloat("_Glossiness", s.smooth);
            }
            return m;
        }

        /// <summary>Makes a material glow (touchpad domes, lights). Works for URP Lit and Standard.</summary>
        public static void SetEmission(Material m, Color c, float intensity)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
        }
    }
}
