// Search field visibility, activation animation, and lowercase-name cache.

using System.Collections;
using TMPro;
using UnityEngine;

namespace MoreHeadBridge;

internal static class CosmeticsSearchHelper
{
    // Lowercases and strips diacritics (á→a, ç→c) so queries match accented names — translated
    // (pt-BR etc.) cosmetic names especially. Applied to both the query and the cached names.
    internal static string FoldText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string formD = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(formD.Length);
        foreach (char c in formD)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    internal static void UpdateSearchFieldVisibility(bool isSearch)
    {
        var field = CosmeticsMenuState.SearchField;
        if (field == null) return;

        if (isSearch)
        {
            CosmeticsMenuState.SetSearchMode(true);
            bool wasActive = field.gameObject.activeSelf;
            field.gameObject.SetActive(true);
            if (!wasActive)
                StartSearchFieldAnimation(field);
        }
        else
        {
            if (CosmeticsMenuState.SearchMode)
                CosmeticsMenuState.ClearSearch();
            field.gameObject.SetActive(false);
        }
    }

    private static void StartSearchFieldAnimation(TMP_InputField field)
    {
        var page = CosmeticsMenuState.ActivePage;
        if (page == null) return;
        page.StartCoroutine(AnimateSearchField(field));
    }

    private static IEnumerator AnimateSearchField(TMP_InputField field)
    {
        var rt = field.GetComponent<RectTransform>();
        var group = field.GetComponent<CanvasGroup>();
        if (group != null)
            group.alpha = 0f;

        yield return new WaitForSeconds(0.05f);

        if (field == null || rt == null || !field.gameObject.activeInHierarchy) yield break;

        if (group != null)
            group.alpha = 1f;

        float duration  = 0.25f;
        float amplitude = 5f;
        float bounces   = 2f;
        float elapsed   = 0f;
        var   basePos   = rt.anchoredPosition;

        while (elapsed < duration)
        {
            if (!field.gameObject.activeInHierarchy) yield break;
            float t    = elapsed / duration;
            float freq = bounces * (1f - t);
            float wave = Mathf.Sin(freq * t * Mathf.PI * 2f);
            float damp = 1f - t;
            rt.anchoredPosition = new Vector2(basePos.x, basePos.y + wave * amplitude * damp);
            elapsed += Time.deltaTime;
            yield return null;
        }

        rt.anchoredPosition = basePos;
    }
}
