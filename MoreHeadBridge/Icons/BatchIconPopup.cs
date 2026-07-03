// MenuLib-backed "Generating Icons" popup for BatchIconGenerator, isolated in its own class: the generator is referenced from always-applied patches, and a MenuLib-typed field made its class load fail without MenuLib (TypeLoadException — class load resolves every field type). Only enter via Plugin.MenuLibAvailable guards (no-inline wrappers in BatchIconGenerator).

using MenuLib;
using MenuLib.MonoBehaviors;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal static class BatchIconPopup
{
    // REPOPopupPage that blocks the cosmetics menu while the batch runs — opened just before the foreach, closed (or destroyed) when the batch ends.
    private static REPOPopupPage?   _page;
    private static TextMeshProUGUI? _progressLabel;

    // Hint text lives in its own TMP, separate from the progress numbers — translation mods can match the hint as a stable string.
    private static TextMeshProUGUI? _hintLabel;

    // Popup label box: total height + the top slice reserved for the numeric progress line. REPOLabel pivots are (0,0), so positions below are bottom-left-relative.
    private const float GeneratingLabelH = 140f;
    private const float NumbersLabelH    = 40f;

    // Tips shown at the bottom of the popup, cycling every 10 seconds; rebuilt each open so AutoCaptureIcons is evaluated at runtime.
    private static string[] _tips = Array.Empty<string>();
    private static int      _currentTipIndex;

    /// Creates and opens the page. The popup sets the cosmetics page to Inactive, blocking all
    /// interaction; ESC routes back to BatchIconGenerator.OnPopupEscape.
    internal static void Open()
    {
        _page = Create();
        _page.OpenPage(openOnTop: false);
        UpdateProgress();
    }

    /// ClosePage restores the cosmetics page (no-op if ESC already cleared the popup).
    internal static void Close()
    {
        if (_page == null) return;
        _page.ClosePage(false);
        Clear();
    }

    /// Destroys the page directly, bypassing MenuManager.PageSetCurrent — which would try to
    /// restore the dying cosmetics page and could cause NullRef errors. For menu-close cleanup.
    internal static void Destroy()
    {
        if (_page == null) return;
        UnityEngine.Object.Destroy(_page.gameObject);
        Clear();
    }

    private static void Clear()
    {
        _page          = null;
        _progressLabel = null;
        _hintLabel     = null;
    }

    /// Updates the numeric progress label only — the ESC hint and tip live in the
    /// hint label (UpdateHintLabel) so their text stays stable for translation mods.
    internal static void UpdateProgress()
    {
        if (_progressLabel == null) return;
        int done  = BatchIconGenerator.ProgressDone + BatchIconGenerator.ProgressFailed;
        int total = BatchIconGenerator.ProgressTotal;
        int pct   = total > 0 ? done * 100 / total : 0;
        string text = $"{done} / {total}  ({pct}%)";
        if (BatchIconGenerator.ProgressFailed > 0) text += $"\n{BatchIconGenerator.ProgressFailed} failed";
        _progressLabel.text = text;
    }

    // Creates the REPOPopupPage that blocks the cosmetics menu during generation (shouldCachePage=false → destroyed on close, no stale state on reopen).
    private static REPOPopupPage Create()
    {
        // localPosition (-120, 0) centres the popup, matching CosmeticOverridePopup.
        var popup = MenuAPI.CreateREPOPopupPage(
            "Generating Icons",
            shouldCachePage: false,
            pageDimmerVisibility: true,
            localPosition: new Vector2(-120f, 0f));

        // Block background MenuScrollBoxes so the popup doesn't double-scroll the menu below.
        {
            var guard = popup.gameObject.AddComponent<PopupScrollGuard>();
            guard.Init(popup.transform);
        }

        _tips            = BuildTipList();
        _currentTipIndex = 0;

        // Progress label, sized to the content mask so tips word-wrap consistently.
        TextMeshProUGUI? capturedLabel  = null;
        TextMeshProUGUI? capturedHint   = null;
        REPOLabel?       capturedParent = null;
        float maskW = popup.maskRectTransform.sizeDelta.x;
        popup.AddElementToScrollView(sv =>
        {
            capturedParent = MenuAPI.CreateREPOLabel("Starting...", sv);
            capturedLabel  = capturedParent.labelTMP;
            if (capturedLabel != null)
            {
                capturedLabel.fontSize  = 16f;
                capturedLabel.alignment = TextAlignmentOptions.Center;
                capturedLabel.enableWordWrapping = true;

                // The element keeps a single 140 px box split into two TMPs: progress numbers on top, ESC hint + tip below (see _hintLabel for why).
                const float LabelWPad = 10f; // pull in from each side to avoid right-edge clipping
                float labelW = (maskW > 0f ? maskW : 200f) - LabelWPad;
                capturedParent.GetComponent<RectTransform>().sizeDelta = new Vector2(labelW, GeneratingLabelH);

                var hintGO = UnityEngine.Object.Instantiate(capturedLabel.gameObject, capturedLabel.transform.parent);
                hintGO.name  = "HintLabel";
                capturedHint = hintGO.GetComponent<TextMeshProUGUI>();

                // Pivots are (0,0): numbers on top, hint below. REPOLabel.Start() re-zeroes the TMP's localPosition a frame later, so CenterScrollElement re-applies the numbers position.
                capturedLabel.rectTransform.sizeDelta     = new Vector2(labelW, NumbersLabelH);
                capturedLabel.rectTransform.localPosition = new Vector3(0f, GeneratingLabelH - NumbersLabelH);
                capturedHint.rectTransform.sizeDelta      = new Vector2(labelW, GeneratingLabelH - NumbersLabelH);
                capturedHint.rectTransform.localPosition  = Vector3.zero;
            }
            return capturedParent.GetComponent<RectTransform>();
        }, topPadding: 0f);

        // One-shot coroutine reads the mask height after one frame and sets topPadding = (maskH − elemH) / 2 to centre the label; setting topPadding auto-triggers UpdateElements().
        if (capturedParent != null)
        {
            var svElement = capturedParent.GetComponent<REPOScrollViewElement>();
            if (svElement != null)
                popup.StartCoroutine(CenterScrollElement(popup, svElement));
        }

        _progressLabel = capturedLabel;
        _hintLabel     = capturedHint;
        UpdateHintLabel();

        // Start tip rotation only when there is more than one tip to cycle through.
        if (_tips.Length > 1)
            popup.StartCoroutine(CycleTips());

        // ESC: the generator stops the batch (coroutine breaks next iteration) and restores the avatar. ESC itself triggers ClosePage, so clear the reference first to prevent a double-close in the generator's finally.
        popup.onEscapePressed = () =>
        {
            Clear();
            return BatchIconGenerator.OnPopupEscape(); // true → allow the popup to close
        };

        return popup;
    }

    // ESC hint + the currently active tip (rotated by CycleTips).
    private static void UpdateHintLabel()
    {
        if (_hintLabel == null) return;
        string text = "Press ESC to stop early";
        if (_tips.Length > 0)
            text += $"\n\n{_tips[_currentTipIndex]}";
        _hintLabel.text = text;
    }

    // Advances the tip every 10 real-time seconds; stops naturally when the batch ends or the label is nulled.
    private static IEnumerator CycleTips()
    {
        while (BatchIconGenerator.IsGenerating && _progressLabel != null)
        {
            yield return new WaitForSecondsRealtime(10f);
            if (!BatchIconGenerator.IsGenerating || _progressLabel == null) yield break;
            _currentTipIndex = (_currentTipIndex + 1) % _tips.Length;
            UpdateHintLabel();
        }
    }

    // Builds the ordered tip list for the current batch popup.
    private static string[] BuildTipList()
    {
        var list = new List<string>
        {
            "Tip: close the menu anytime to pause — reopen it to resume where you left off",
        };
        if (Plugin.AutoCaptureIcons.Value)
            list.Add("Tip: hovering a cosmetic with no icon captures it automatically");
        return list.ToArray();
    }

    // Waits one frame for layout, then sets the topPadding that centres the element in the popup's mask (auto-triggers UpdateElements).
    private static IEnumerator CenterScrollElement(
        REPOPopupPage popup, REPOScrollViewElement element)
    {
        yield return null; // one frame for RectTransform dimensions to be applied

        // REPOLabel.Start() (just ran) reset the numbers TMP to localPosition zero — put it back on the top slice of the label box.
        if (_progressLabel != null)
            _progressLabel.rectTransform.localPosition =
                new Vector3(0f, GeneratingLabelH - NumbersLabelH);

        float maskH = popup.maskRectTransform.sizeDelta.y;
        float elemH = element.rectTransform.rect.height;
        if (elemH > 0f)
            element.topPadding = Mathf.Max(0f, (maskH - elemH) / 2f);
    }
}
