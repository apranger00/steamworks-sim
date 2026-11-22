using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SteamworksSim.UI
{
    /// <summary>Clickable graphic that is not a Selectable, so it never steals keyboard/gamepad focus.</summary>
    public class ClickArea : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();
    }
}
