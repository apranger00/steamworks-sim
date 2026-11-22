using System;
using SteamworksSim.Game;
using SteamworksSim.InputHandling;
using UnityEngine;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    /// <summary>
    /// Full-screen final score: a gray veil fades in over the field (left as it was at 0:00) and the score
    /// card slides in. Totals, an AUTO / TELEOP breakdown per alliance, the robots on each side, ranking
    /// points, and the winner.
    /// </summary>
    public class ResultsScreen : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public Selectable First { get; private set; }

        Image veil;
        RectTransform card;
        float shownAt;

        Text redTotal, blueTotal, footer;
        readonly Text[] redCells = new Text[RowCount], blueCells = new Text[RowCount];
        Side red, blue;

        class Side
        {
            public GameObject winner;
            public Text winnerText, stats;
            public readonly Text[] robots = new Text[3];
            public readonly Image[] slots = new Image[3];
        }

        // Breakdown rows (null label = section header)
        static readonly string[] Rows = { "AUTONOMOUS", "Mobility", "Fuel", "Rotors", "TELEOPERATED", "Fuel", "Rotors", "Takeoff", "PENALTY" };
        const int RowCount = 9;
        static readonly bool[] Header = { true, false, false, false, true, false, false, false, true };

        static readonly Color RedDark = new Color(0.45f, 0.07f, 0.08f, 0.94f), BlueDark = new Color(0.05f, 0.17f, 0.38f, 0.94f);
        static readonly Color RedBright = new Color(0.86f, 0.13f, 0.15f, 1f), BlueBright = new Color(0.06f, 0.36f, 0.72f, 1f);
        static readonly Color Winner = new Color(1f, 0.87f, 0.0f, 1f);
        static readonly Color RowLight = new Color(0.97f, 0.97f, 0.98f, 1f), RowHeader = new Color(0.86f, 0.87f, 0.89f, 1f);

        public RectTransform Build(Transform parent, Action playAgain, Action robotSelect, Action mainMenu)
        {
            Root = Ui.Stretch(Ui.Rect("Results", parent));
            veil = Ui.Fill(Root, "Veil", new Color(0.12f, 0.13f, 0.15f, 0.6f));

            card = Ui.Anchor(Ui.Rect("Card", Root), new Vector2(0.5f, 0.5f), new Vector2(1760f, 960f));

            // Title band
            var band = Ui.Anchor(Ui.Rect("Band", card), new Vector2(0.5f, 1f), new Vector2(1760f, 84f));
            Ui.Round(band.gameObject.AddComponent<Image>(), 14f).color = new Color(0.1f, 0.11f, 0.13f, 0.96f);
            var title = Ui.Title(band, "FINAL SCORE", 54, Ui.TextColor);
            Ui.Stretch(title.rectTransform);

            // Columns: red side | score table | blue side
            const float top = -100f, sideW = 500f, midW = 720f, gap = 20f, colH = 700f;
            red = BuildSide(card, new Vector2(-(midW / 2f + gap + sideW / 2f), top), sideW, colH, RedDark, RedBright, "RED ALLIANCE");
            blue = BuildSide(card, new Vector2(midW / 2f + gap + sideW / 2f, top), sideW, colH, BlueDark, BlueBright, "BLUE ALLIANCE");

            var mid = Ui.Anchor(Ui.Rect("Table", card), new Vector2(0.5f, 1f), new Vector2(midW, colH), new Vector2(0f, top));
            var totals = Ui.Anchor(Ui.Rect("Totals", mid), new Vector2(0.5f, 1f), new Vector2(midW, 180f));
            redTotal = TotalBox(totals, new Vector2(0f, 0.5f), RedBright, "RED");
            blueTotal = TotalBox(totals, new Vector2(1f, 0.5f), BlueBright, "BLUE");

            var table = Ui.Column(mid, "Rows", midW, 0f, 0, false);
            Ui.Anchor(table, new Vector2(0.5f, 1f), new Vector2(midW, colH - 190f), new Vector2(0f, -190f));
            for (int i = 0; i < RowCount; i++)
            {
                var row = Ui.Row(table, Header[i] ? 50f : 52f, 0f);
                row.gameObject.AddComponent<Image>().color = Header[i] ? RowHeader : RowLight;
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(28, 28, 0, 0);
                redCells[i] = Ui.Title(row, "", Header[i] ? 30 : 34, Ui.Ink, TextAnchor.MiddleLeft);
                Ui.SetWidth(redCells[i], 120f);
                var label = Header[i] ? Ui.Title(row, Rows[i], 30, Ui.Ink) : Ui.Label(row, Rows[i].ToUpper(), 24, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                Ui.SetWidth(label, 300f, 1f);
                blueCells[i] = Ui.Title(row, "", Header[i] ? 30 : 34, Ui.Ink, TextAnchor.MiddleRight);
                Ui.SetWidth(blueCells[i], 120f);
            }

            // Buttons
            var buttons = Ui.Anchor(Ui.Rect("Buttons", card), new Vector2(0.5f, 0f), new Vector2(1100f, 64f), new Vector2(0f, 40f));
            var h2 = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            h2.spacing = 20f; h2.childControlWidth = true; h2.childControlHeight = true; h2.childForceExpandWidth = true; h2.childForceExpandHeight = true;
            First = Ui.Button(buttons, "PLAY AGAIN", () => playAgain(), 64f, 30, true, true);
            Ui.Button(buttons, "ROBOT SELECTION", () => robotSelect(), 64f, 30, true, true);
            Ui.Button(buttons, "MAIN MENU", () => mainMenu(), 64f, 30, true, true);
            footer = Ui.Label(card, "", 20, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Anchor(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(1400f, 30f), new Vector2(0f, 6f));
            footer.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1f, -1f);
            return Root;
        }

        static Text TotalBox(RectTransform parent, Vector2 at, Color color, string name)
        {
            var box = Ui.Anchor(Ui.Rect(name, parent), at, new Vector2(358f, 180f));
            box.gameObject.AddComponent<Image>().color = color;
            var label = Ui.Title(box, name, 34, Ui.TextColor, TextAnchor.UpperCenter);
            Ui.Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(358f, 50f), new Vector2(0f, -10f));
            var total = Ui.Title(box, "0", 104, Ui.TextColor);
            Ui.Anchor(total.rectTransform, new Vector2(0.5f, 0f), new Vector2(358f, 130f), new Vector2(0f, 6f));
            return total;
        }

        static Side BuildSide(RectTransform card, Vector2 pos, float w, float h, Color dark, Color bright, string name)
        {
            var s = new Side();
            var r = Ui.Anchor(Ui.Rect(name, card), new Vector2(0.5f, 1f), new Vector2(w, h), pos);
            r.gameObject.AddComponent<Image>().color = dark;

            // winner badge (or TIE)
            var badge = Ui.Anchor(Ui.Rect("Winner", r), new Vector2(0.5f, 1f), new Vector2(w, 180f));
            badge.gameObject.AddComponent<Image>().color = Winner;
            s.winnerText = Ui.Title(badge, "WINNER", 88, Ui.Ink);
            Ui.Stretch(s.winnerText.rectTransform);
            s.winner = badge.gameObject;

            var title = Ui.Title(r, name, 34, Ui.TextColor);
            Ui.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(w, 50f), new Vector2(0f, -205f));

            for (int i = 0; i < 3; i++)
            {
                var slot = Ui.Anchor(Ui.Rect("Robot " + (i + 1), r), new Vector2(0.5f, 1f), new Vector2(w - 60f, 62f), new Vector2(0f, -270f - i * 74f));
                s.slots[i] = slot.gameObject.AddComponent<Image>();
                s.slots[i].color = bright;
                s.robots[i] = Ui.Title(slot, "", 34, Ui.TextColor);
                Ui.Stretch(s.robots[i].rectTransform, 6f);
            }

            s.stats = Ui.Label(r, "", 22, Ui.TextColor, TextAnchor.UpperCenter);
            Ui.Anchor(s.stats.rectTransform, new Vector2(0.5f, 0f), new Vector2(w - 40f, 130f), new Vector2(0f, 20f));
            return s;
        }

        /// <summary>Fills in the score and starts the fade + slide.</summary>
        public void Show(MatchScore score)
        {
            redTotal.text = score.red.Total.ToString();
            blueTotal.text = score.blue.Total.ToString();
            Fill(redCells, score.red);
            Fill(blueCells, score.blue);
            string w = score.Winner;
            FillSide(red, score.red, score, w);
            FillSide(blue, score.blue, score, w);
            footer.text = $"{GameControls.Both(GameControls.RestartMatch)}: play again";
            shownAt = Time.unscaledTime;
            Animate(0f);
        }

        static void Fill(Text[] cells, AllianceScore a)
        {
            int[] v = { a.Auto, a.mobility, a.autoFuel, a.autoRotors, a.Teleop, a.teleopFuel, a.teleopRotors, a.takeoff, a.penalty };
            for (int i = 0; i < RowCount; i++) cells[i].text = v[i].ToString();
        }

        static void FillSide(Side s, AllianceScore a, MatchScore score, string winner)
        {
            s.winner.SetActive(winner == a.alliance || winner == "tie");
            s.winnerText.text = winner == "tie" ? "TIE" : "WINNER";
            for (int i = 0; i < 3; i++)
            {
                bool has = i < a.robots.Count;
                s.robots[i].text = has ? a.robots[i] : "";
                var c = s.slots[i].color; c.a = has ? 1f : 0.35f; s.slots[i].color = c;
            }
            s.stats.text = $"Pressure {a.kPa:0.#} / 40 kPa{(a.PressureRp ? "  (+1 RP)" : "")}\n" +
                           $"Rotors turning {a.rotorsTurning} / 4{(a.RotorsRp ? "  (+1 RP)" : "")}\n" +
                           $"Ranking points: {score.RankingPoints(a)}";
        }

        void Update()
        {
            if (Root != null && Root.gameObject.activeInHierarchy) Animate(Time.unscaledTime - shownAt);
        }

        void Animate(float t)
        {
            // veil fades in over 0.35 s; card slides in from the right over 0.55 s, starting at 0.15 s
            var vc = veil.color; vc.a = 0.6f * Mathf.Clamp01(t / 0.35f); veil.color = vc;
            float k = Mathf.Clamp01((t - 0.15f) / 0.55f);
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            card.anchoredPosition = new Vector2(Mathf.Lerp(2000f, 0f, ease), 0f);
        }
    }
}
