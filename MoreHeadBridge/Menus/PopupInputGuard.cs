using System.Collections.Generic;
using System.Linq;
using MenuLib.MonoBehaviors;
using UnityEngine;

namespace MoreHeadBridge;

// Disables every interactable element OUTSIDE the popup; reference-counted (nested popups stack) and restored on disable/destroy — covers every close path.
internal sealed class PopupInputGuard : MonoBehaviour
{
    private static readonly Dictionary<MenuButton, int> MenuButtonLocks = new();
    private static readonly Dictionary<MenuButton, bool> MenuButtonEnabledState = new();
    private static readonly Dictionary<MenuSlider, int> MenuSliderLocks = new();
    private static readonly Dictionary<MenuSlider, bool> MenuSliderEnabledState = new();
    private static readonly Dictionary<REPOSlider, int> RepoSliderLocks = new();
    private static readonly Dictionary<REPOSlider, bool> RepoSliderEnabledState = new();
    private static readonly Dictionary<MenuElementHover, int> HoverLocks = new();
    private static readonly Dictionary<MenuElementHover, bool> HoverEnabledState = new();
    private readonly List<MenuButton> _menuButtons = new();
    private readonly List<MenuSlider> _menuSliders = new();
    private readonly List<REPOSlider> _repoSliders = new();
    private readonly List<MenuElementHover> _hoverTargets = new();
    private string _popupName = "Unknown";

    internal void Init(Transform popupRoot)
    {
        _menuButtons.Clear();
        _menuSliders.Clear();
        _repoSliders.Clear();
        _hoverTargets.Clear();
        _popupName = popupRoot.gameObject.name;
        _menuButtons.AddRange(UnityEngine.Object.FindObjectsOfType<MenuButton>(true)
            .Where(b => b != null && !b.transform.IsChildOf(popupRoot)));
        _menuSliders.AddRange(UnityEngine.Object.FindObjectsOfType<MenuSlider>(true)
            .Where(s => s != null && !s.transform.IsChildOf(popupRoot)));
        _repoSliders.AddRange(UnityEngine.Object.FindObjectsOfType<REPOSlider>(true)
            .Where(s => s != null && !s.transform.IsChildOf(popupRoot)));
        // Keep the avatar-preview hover enabled so click+drag rotation still works while a popup
        // is open (its MenuElementHover shares the GameObject with PlayerAvatarMenuHover).
        _hoverTargets.AddRange(UnityEngine.Object.FindObjectsOfType<MenuElementHover>(true)
            .Where(h => h != null && !h.transform.IsChildOf(popupRoot)
                        && h.GetComponent<PlayerAvatarMenuHover>() == null));

        foreach (var menuButton in _menuButtons)
        {
            if (MenuButtonLocks.TryGetValue(menuButton, out int count))
            {
                MenuButtonLocks[menuButton] = count + 1;
            }
            else
            {
                MenuButtonLocks[menuButton] = 1;
                MenuButtonEnabledState[menuButton] = menuButton.enabled;
                menuButton.enabled = false;
            }
        }

        foreach (var menuSlider in _menuSliders)
        {
            if (MenuSliderLocks.TryGetValue(menuSlider, out int count))
            {
                MenuSliderLocks[menuSlider] = count + 1;
            }
            else
            {
                MenuSliderLocks[menuSlider] = 1;
                MenuSliderEnabledState[menuSlider] = menuSlider.enabled;
                menuSlider.enabled = false;
            }
        }

        foreach (var repoSlider in _repoSliders)
        {
            if (RepoSliderLocks.TryGetValue(repoSlider, out int count))
            {
                RepoSliderLocks[repoSlider] = count + 1;
            }
            else
            {
                RepoSliderLocks[repoSlider] = 1;
                RepoSliderEnabledState[repoSlider] = repoSlider.enabled;
                repoSlider.enabled = false;
            }
        }

        foreach (var hover in _hoverTargets)
        {
            if (HoverLocks.TryGetValue(hover, out int count))
            {
                HoverLocks[hover] = count + 1;
            }
            else
            {
                HoverLocks[hover] = 1;
                HoverEnabledState[hover] = hover.enabled;
                hover.enabled = false;
            }
        }
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    private void Restore()
    {
        foreach (var menuButton in _menuButtons)
        {
            if (menuButton == null) continue;
            if (!MenuButtonLocks.TryGetValue(menuButton, out int count)) continue;

            if (count <= 1)
            {
                MenuButtonLocks.Remove(menuButton);
                bool wasEnabled = MenuButtonEnabledState.TryGetValue(menuButton, out var v) && v;
                MenuButtonEnabledState.Remove(menuButton);
                menuButton.enabled = wasEnabled;
            }
            else
            {
                MenuButtonLocks[menuButton] = count - 1;
            }
        }

        foreach (var menuSlider in _menuSliders)
        {
            if (menuSlider == null) continue;
            if (!MenuSliderLocks.TryGetValue(menuSlider, out int count)) continue;

            if (count <= 1)
            {
                MenuSliderLocks.Remove(menuSlider);
                bool wasEnabled = MenuSliderEnabledState.TryGetValue(menuSlider, out var v) && v;
                MenuSliderEnabledState.Remove(menuSlider);
                menuSlider.enabled = wasEnabled;
            }
            else
            {
                MenuSliderLocks[menuSlider] = count - 1;
            }
        }

        foreach (var repoSlider in _repoSliders)
        {
            if (repoSlider == null) continue;
            if (!RepoSliderLocks.TryGetValue(repoSlider, out int count)) continue;

            if (count <= 1)
            {
                RepoSliderLocks.Remove(repoSlider);
                bool wasEnabled = RepoSliderEnabledState.TryGetValue(repoSlider, out var v) && v;
                RepoSliderEnabledState.Remove(repoSlider);
                repoSlider.enabled = wasEnabled;
            }
            else
            {
                RepoSliderLocks[repoSlider] = count - 1;
            }
        }

        foreach (var hover in _hoverTargets)
        {
            if (hover == null) continue;
            if (!HoverLocks.TryGetValue(hover, out int count)) continue;

            if (count <= 1)
            {
                HoverLocks.Remove(hover);
                bool wasEnabled = HoverEnabledState.TryGetValue(hover, out var v) && v;
                HoverEnabledState.Remove(hover);
                hover.enabled = wasEnabled;
            }
            else
            {
                HoverLocks[hover] = count - 1;
            }
        }

        _menuButtons.Clear();
        _menuSliders.Clear();
        _repoSliders.Clear();
        _hoverTargets.Clear();
    }
}
