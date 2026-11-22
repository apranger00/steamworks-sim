using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    /// <summary>
    /// A value you change with left/right (keyboard arrows, D-pad, stick, or the arrow labels with the mouse).
    /// Up/down move to the next row as usual. Submit steps forward.
    /// </summary>
    public class OptionRow : Selectable, ISubmitHandler
    {
        Text valueText;
        string[] values = new string[0];
        Action<int> onChange;
        bool wrap = true;

        public int Index { get; private set; }

        public void Setup(Text text, string[] newValues, int index, Action<int> changed, bool wrapAround)
        {
            valueText = text; onChange = changed; wrap = wrapAround;
            SetValues(newValues, index);
        }

        public void SetValues(string[] newValues, int index)
        {
            values = newValues ?? new string[0];
            Index = values.Length == 0 ? 0 : Mathf.Clamp(index, 0, values.Length - 1);
            Refresh();
        }

        public void SetIndex(int index)
        {
            if (values.Length == 0) return;
            Index = Mathf.Clamp(index, 0, values.Length - 1);
            Refresh();
        }

        public void Step(int delta)
        {
            if (values.Length == 0 || !IsInteractable()) return;
            int n = values.Length, next = Index + delta;
            next = wrap ? ((next % n) + n) % n : Mathf.Clamp(next, 0, n - 1);
            if (next == Index) return;
            Index = next;
            Refresh();
            onChange?.Invoke(Index);
        }

        void Refresh()
        {
            if (valueText != null) valueText.text = values.Length > 0 ? values[Index] : "-";
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { Step(-1); return; }
            if (eventData.moveDir == MoveDirection.Right) { Step(1); return; }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData) => Step(1);
    }
}
