using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MoreHeadBridge;

// Click proxy for a slot button — forwards clicks/hover to the owning BridgeSlotSelectorRow.
internal sealed class SlotButtonProxy : MonoBehaviour
{
    internal BridgeSlotSelectorRow? row;
    internal int slotIndex;
    internal RectTransform? labelRT;
    internal float baseY;

    private MenuButton? _btn;
    private bool _wasClicked;

    private void Awake() => _btn = GetComponent<MenuButton>();

    private void Update()
    {
        bool clicked = _btn != null && _btn.clicked;
        if (!clicked) { _wasClicked = false; return; }
        if (_wasClicked) return;
        _wasClicked = true;

        MenuManager.instance?.MenuEffectClick(MenuManager.MenuClickEffectType.Confirm);
        row?.OnSlotClicked(slotIndex);
    }

    private void LateUpdate() => HoverAdjust();

    private void HoverAdjust()
    {
        if (_btn == null || labelRT == null) return;
        var pos = labelRT.anchoredPosition;
        float targetY = _btn.hovering ? baseY + 1f : baseY;
        if (Mathf.Approximately(pos.y, targetY)) return;
        labelRT.anchoredPosition = new Vector2(pos.x, targetY);
    }
}
