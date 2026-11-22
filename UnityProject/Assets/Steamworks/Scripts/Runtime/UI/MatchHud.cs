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
        Image timerBg, countdownFill;
        RectTransform countdownRoot;
        Text countdownNumber;
        float bannerAt = -100f, hintRefresh, toastAt = -100f;
        Text toast;
        class ScoreBox { public Text total, kpa; public Image[] rotors; public GameObject rp; }
        ScoreBox redBox, blueBox;

        public void Init(SimBootstrap owner)
        {
            sim = owner;
            var canvas = Ui.Canvas("Match HUD", 0, transform);
            root = canvas.gameObject;

            var box = Ui.Anchor(Ui.Rect("Timer", canvas.transform), new Vector2(0.5f, 1f), new Vector2(340f, 116f), new Vector2(0f, -14f));
            timerBg = box.gameObject.AddComponent<Image>();
            Ui.Round(timerBg, 16f);
            timerBg.color = Ui.Backdrop;
            period = Ui.Label(box, "", 22, Ui.Muted, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Anchor(period.rectTransform, new Vector2(0.5f, 1f), new Vector2(340f, 30f), new Vector2(0f, -8f));
            time = Ui.Title(box, "", 66, Ui.TextColor);
            Ui.Anchor(time.rectTransform, new Vector2(0.5f, 0f), new Vector2(340f, 80f), new Vector2(0f, 4f));

            redBox = BuildScore(canvas.transform, -1, new Color(0.78f, 0.12f, 0.14f, 0.92f));
            blueBox = BuildScore(canvas.transform, 1, new Color(0.08f, 0.33f, 0.7f, 0.92f));
            toast = Ui.Label(canvas.transform, "", 26, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Anchor(toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(1200f, 40f), new Vector2(0f, -146f));
            toast.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            banner = Ui.Title(canvas.transform, "", 64, Ui.Gold);
            Ui.Anchor(banner.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(1400f, 90f));
            banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            centerHint = Ui.Label(canvas.transform, "", 30, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Anchor(centerHint.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(1400f, 50f));
            centerHint.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            // 3-2-1 countdown: big number inside a ring that drains each second
            countdownRoot = Ui.Anchor(Ui.Rect("Countdown", canvas.transform), new Vector2(0.5f, 0.5f), new Vector2(320f, 320f), new Vector2(0f, 40f));
            var disc = Ui.Rect("Disc", countdownRoot);
            Ui.Stretch(disc, 18f);
            var discImg = disc.gameObject.AddComponent<Image>();
            discImg.sprite = Ui.Disc; discImg.color = new Color(0.05f, 0.06f, 0.08f, 0.6f); discImg.raycastTarget = false;
            var track = Ui.Fill(countdownRoot, "Track", new Color(1f, 1f, 1f, 0.15f));
            track.sprite = Ui.Ring; track.raycastTarget = false;
            countdownFill = Ui.Fill(countdownRoot, "Fill", Ui.Gold);
            countdownFill.sprite = Ui.Ring; countdownFill.raycastTarget = false;
            countdownFill.type = Image.Type.Filled;
            countdownFill.fillMethod = Image.FillMethod.Radial360;
            countdownFill.fillOrigin = (int)Image.Origin360.Top;
            countdownFill.fillClockwise = false;
            countdownNumber = Ui.Title(countdownRoot, "3", 190, Ui.TextColor);
            Ui.Stretch(countdownNumber.rectTransform);
            countdownNumber.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -3f);
            countdownRoot.gameObject.SetActive(false);

            cornerHint = Ui.Label(canvas.transform, "", 18, Ui.Muted, TextAnchor.LowerLeft);
            Ui.Anchor(cornerHint.rectTransform, new Vector2(0f, 0f), new Vector2(900f, 30f), new Vector2(20f, 14f));
            SetVisible(false);
        }

        public void SetVisible(bool v) { if (root != null) root.SetActive(v); }

        public void Banner(string text) { banner.text = text; bannerAt = Time.unscaledTime; }

        public void ClearBanner() { bannerAt = -100f; }

        /// <summary>A short message under the timer (rotor active, gear placed, ...).</summary>
        public void Toast(string text, Color color) { toast.text = text; toast.color = color; toastAt = Time.unscaledTime; }

        // Alliance score next to the timer: total, pressure (x / 40 kPa until the ranking point, then an RP badge)
        // and four ROTOR dots.
        static ScoreBox BuildScore(Transform parent, int side, Color color)
        {
            var b = new ScoreBox();
            var box = Ui.Anchor(Ui.Rect(side < 0 ? "Red score" : "Blue score", parent), new Vector2(0.5f, 1f), new Vector2(250f, 116f), new Vector2(side * 305f, -14f));
            Ui.Round(box.gameObject.AddComponent<Image>(), 16f).color = color;
            b.total = Ui.Title(box, "0", 58, Ui.TextColor);
            Ui.Anchor(b.total.rectTransform, new Vector2(0.5f, 1f), new Vector2(250f, 66f), new Vector2(0f, -4f));
            b.kpa = Ui.Label(box, "0 / 40 kPa", 19, Ui.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold);
            Ui.Anchor(b.kpa.rectTransform, new Vector2(0f, 0f), new Vector2(150f, 34f), new Vector2(14f, 8f));
            var rp = Ui.Rect("RP", box);
            Ui.Anchor(rp, new Vector2(0f, 0f), new Vector2(42f, 24f), new Vector2(104f, 13f));
            Ui.Round(rp.gameObject.AddComponent<Image>(), 12f).color = new Color(1f, 0.87f, 0.1f);
            var rpText = Ui.Label(rp, "RP", 15, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(rpText.rectTransform);
            b.rp = rp.gameObject; b.rp.SetActive(false);
            b.rotors = new Image[4];
            for (int k = 0; k < 4; k++)
            {
                var dot = Ui.Rect("Rotor " + (k + 1), box);
                Ui.Anchor(dot, new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(-14f - (3 - k) * 21f, 17f));
                b.rotors[k] = dot.gameObject.AddComponent<Image>();
                b.rotors[k].sprite = Ui.Disc;
            }
            return b;
        }

        static void FillScore(ScoreBox b, Game.AllianceScore a)
        {
            b.total.text = a.Total.ToString();
            bool rp = a.PressureRp;
            b.kpa.text = rp ? $"{Mathf.FloorToInt(a.kPa)} kPa" : $"{Mathf.FloorToInt(a.kPa)} / 40 kPa";
            if (b.rp.activeSelf != rp) b.rp.SetActive(rp);
            for (int k = 0; k < 4; k++) b.rotors[k].color = k < a.rotorsTurning ? new Color(1f, 0.85f, 0.15f) : new Color(1f, 1f, 1f, 0.25f);
        }

        void Update()
        {
            if (root == null || !root.activeSelf) return;
            var c = sim.Clock;
            switch (c.Phase)
            {
                case MatchPhase.Countdown: period.text = "GET READY"; period.color = Ui.Gold; break;
                case MatchPhase.Auto: period.text = "AUTONOMOUS"; period.color = Ui.Gold; break;
                case MatchPhase.AutoPause: period.text = "TELEOP"; period.color = Ui.Muted; break;
                case MatchPhase.Teleop:
                    period.text = c.InEndgame ? "ENDGAME" : "TELEOPERATED";
                    period.color = c.InEndgame ? new Color(1f, 0.55f, 0.2f) : Ui.Muted;
                    break;
                case MatchPhase.Over: period.text = "MATCH OVER"; period.color = Ui.Muted; break;
                default: period.text = ""; break;
            }
            time.text = Format(c.PeriodLeft);
            time.color = c.InEndgame && c.Phase == MatchPhase.Teleop ? new Color(1f, 0.55f, 0.2f) : Ui.TextColor;

            bool counting = c.Phase == MatchPhase.Countdown;
            if (countdownRoot.gameObject.activeSelf != counting) countdownRoot.gameObject.SetActive(counting);
            if (counting)
            {
                float left = c.CountdownLeft;
                int n = Mathf.Clamp(Mathf.CeilToInt(left - 1e-4f), 1, 3);
                float frac = Mathf.Clamp01(left - (n - 1));          // 1 -> 0 within each second
                countdownNumber.text = n.ToString();
                countdownFill.fillAmount = frac;
                float pop = Mathf.Clamp01((frac - 0.8f) / 0.2f);     // each new number pops in
                countdownNumber.rectTransform.localScale = Vector3.one * (1f + 0.45f * pop * pop);
                var nc = countdownNumber.color; nc.a = 1f - pop * 0.6f; countdownNumber.color = nc;
            }

            FillScore(redBox, sim.Score.red);
            FillScore(blueBox, sim.Score.blue);
            var tc = toast.color; tc.a = Mathf.Clamp01(1f - (Time.unscaledTime - toastAt - 2.2f) / 0.5f); toast.color = tc;

            float age = Time.unscaledTime - bannerAt;
            var bc = banner.color; bc.a = Mathf.Clamp01(1f - (age - 1.6f) / 0.6f);
            banner.color = bc;

            if (Time.unscaledTime >= hintRefresh)
            {
                hintRefresh = Time.unscaledTime + 1f;
                cornerHint.text = $"{GameControls.Both(GameControls.RestartMatch)}: restart match   ·   {GameControls.Both(GameControls.Pause)}: pause";
            }
            if (centerHint.text.Length > 0) centerHint.text = ""; // the final score screen takes over at 0:00
        }

        static string Format(float seconds)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds - 1e-4f));
            return $"{t / 60}:{t % 60:00}";
        }
    }
}
