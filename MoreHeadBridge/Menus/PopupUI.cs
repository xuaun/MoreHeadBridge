using System;
using System.Collections;
using System.Globalization;
using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;

namespace MoreHeadBridge;

// Shared layout constants, row factory, guard wiring and slider-numeric helpers for the mod's MenuLib popups.
internal static class PopupUI
{
    internal const float PopupX = -120f;
    internal const float TitleGap = 15f;
    internal const float BtnTopGap = 10f;
    internal const float BtnRowH = 30f;
    internal const float ButtonLeftX = -137f;   // left column (Back / Cancel)
    internal const float ButtonRightX = 58f;    // right column (Save / Done)

    // Creates a bare RectTransform sized for one button row, parented to the scroll view; buttons are added as children with manual localPosition offsets.
    internal static RectTransform MakeRow(Transform parent)
    {
        var row = new GameObject("Button Row", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(parent, false);
        row.sizeDelta = new Vector2(0f, BtnRowH);
        return row;
    }

    // Attaches the standard popup guards: scroll-block always, input-block optionally, and a local dimmer over the parent when this is a sub-popup. All restore on close.
    internal static void AttachGuards(REPOPopupPage popup, Transform? parentPopupTransform = null, bool inputGuard = true)
    {
        popup.gameObject.AddComponent<PopupScrollGuard>().Init(popup.transform);
        if (inputGuard)
            popup.gameObject.AddComponent<PopupInputGuard>().Init(popup.transform);
        if (parentPopupTransform != null)
            LocalPopupOverlay.Add(popup.gameObject, parentPopupTransform);
    }

    // ── Slider-numeric helpers (string option ↔ value) ──────────────────────────

    internal static float ParseFloat(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

    internal static string ClosestFloat(string[] options, float target)
    {
        string best = options[0];
        float bestDist = float.MaxValue;
        foreach (var opt in options)
        {
            float dist = Mathf.Abs(ParseFloat(opt) - target);
            if (dist < bestDist) { bestDist = dist; best = opt; }
        }
        return best;
    }

    internal static string ClosestInt(string[] options, int target)
    {
        string best = options[0];
        int bestDist = int.MaxValue;
        foreach (var opt in options)
        {
            if (!int.TryParse(opt, out int v)) continue;
            int dist = Math.Abs(v - target);
            if (dist < bestDist) { bestDist = dist; best = opt; }
        }
        return best;
    }

    // Adds a numeric slider to the popup whose discrete string options snap to the nearest float value.
    internal static void AddFloatSlider(
        REPOPopupPage popup, string label, string[] options, float current,
        Action<float> onChange, float topPadding = 0f)
    {
        popup.AddElementToScrollView(scrollView =>
        {
            var s = MenuAPI.CreateREPOSlider(label, "",
                (string opt) => onChange(ParseFloat(opt)),
                scrollView, options, ClosestFloat(options, current));
            return (RectTransform)s.transform;
        }, topPadding: topPadding);
    }

    // Adds a numeric slider to the popup whose discrete string options snap to the nearest int value.
    internal static void AddIntSlider(
        REPOPopupPage popup, string label, string[] options, int current,
        Action<float> onChange, float topPadding = 0f)
    {
        popup.AddElementToScrollView(scrollView =>
        {
            var s = MenuAPI.CreateREPOSlider(label, "",
                (string opt) => onChange(ParseFloat(opt)),
                scrollView, options, ClosestInt(options, current));
            return (RectTransform)s.transform;
        }, topPadding: topPadding);
    }

    // Runs <paramref name="open"/> only after the left mouse button is released: REPO buttons fire on mouse-DOWN and a MenuLib slider scrubs while held, so a slider landing under the cursor gets silently dragged. Deferring just OpenPage is NOT enough — pages are ACTIVE from CreateREPOPopupPage on, REPOSlider.Update runs before open, and UIMouseHover doesn't require an open page — so defer the whole popup BUILD with this helper.
    internal static void AfterMouseRelease(MonoBehaviour host, Action open)
    {
        if (host == null) { open(); return; }
        host.StartCoroutine(AfterMouseReleaseRoutine(open));
    }

    private static IEnumerator AfterMouseReleaseRoutine(Action open)
    {
        while (Input.GetMouseButton(0)) yield return null;
        yield return null; // one extra frame so the release is fully processed
        open();
    }
}
