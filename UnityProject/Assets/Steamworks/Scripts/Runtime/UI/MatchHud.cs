using SteamworksSim.Game;
using SteamworksSim.InputHandling;
using UnityEngine;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    /// <summary>Match timer, period banners and control hints (scores join in step 3).</summary>
    public class MatchHud : MonoBehaviour
    {
        SimBootstrap sim;
        GameObject root;
        Text period, time, banner, centerHint, cornerHint;
        Image timerBg;
        float bannerAt = -100f, hintRefresh;

        public void Init(SimBootstrap owner)
        {
            sim = owner;
            var canvas = Ui.Canvas("Match HUD", 0, transform);
            root = canvas.gameObject;

            var box = Ui.Anchor(Ui.Rect("Timer", canvas.transform), new Vector2(0.5f, 1f), new Vector2(340f, 116f), new Vector2(0f, -14f));
            timerBg = box.gameObject.AddComponent<Image>();
            timerBg.color = Ui.Backdrop;
            period = Ui.Label(box, "", 22, Ui.Muted, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Anchor(period.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 30f), new Vector2(0f, -8f));
            time = Ui.Label(box, "", 62, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Anchor(time.rectTransform, new Vector2(0.5f, 0f), new Vector2(340f, 80f), new Vector2(0f, 4f));

            banner = Ui.Label(canvas.transform, "", 54, Ui.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Anchor(banner.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(1400f, 90f));
            banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            centerHint = Ui.Label(canvas.transform, "", 30, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Anchor(centerHint.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(1400f, 50f));
            centerHint.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            cornerHint = Ui.Label(canvas.transform, "", 18, Ui.Muted, TextAnchor.LowerLeft);
            Ui.Anchor(cornerHint.rectTransform, new Vector2(0f, 0f), new Vector2(900f, 30f), new Vector2(20f, 14f));
            SetVisible(false);
        }

        public void SetVisible(bool v) { if (root != null) root.SetActive(v); }

        public void Banner(string text) { banner.text = text; bannerAt = Time.unscaledTime; }

        public void ClearBanner() { bannerAt = -100f; }

        void Update()
        {
            if (root == null || !root.activeSelf) return;
            var c = sim.Clock;
            switch (c.Phase)
            {
                case MatchPhase.Auto: period.text = "AUTONOMOUS"; period.color = Ui.Gold; break;
                case MatchPhase.Teleop:
                    period.text = c.InEndgame ? "ENDGAME" : "TELEOPERATED";
                    period.color = c.InEndgame ? new Color(1f, 0.55f, 0.2f) : Ui.Muted;
                    break;
                case MatchPhase.Over: period.text = "MATCH OVER"; period.color = Ui.Muted; break;
                default: period.text = ""; break;
            }
            time.text = Format(c.PeriodLeft);
            time.color = c.InEndgame && c.Phase == MatchPhase.Teleop ? new Color(1f, 0.55f, 0.2f) : Ui.TextColor;

            float age = Time.unscaledTime - bannerAt;
            var bc = banner.color; bc.a = Mathf.Clamp01(1f - (age - 1.6f) / 0.6f);
            banner.color = bc;

            if (Time.unscaledTime >= hintRefresh)
            {
                hintRefresh = Time.unscaledTime + 1f;
                cornerHint.text = $"{GameControls.Both(GameControls.RestartMatch)}: restart match   ·   {GameControls.Both(GameControls.Pause)}: pause";
            }
            if (c.Phase == MatchPhase.Over) { if (centerHint.text.Length == 0) centerHint.text = $"Press {GameControls.Both(GameControls.RestartMatch)} to play again"; }
            else if (centerHint.text.Length > 0) centerHint.text = "";
        }

        static string Format(float seconds)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds - 1e-4f));
            return $"{t / 60}:{t % 60:00}";
        }
    }
}
