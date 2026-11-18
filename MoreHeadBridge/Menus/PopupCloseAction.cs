using UnityEngine;

namespace MoreHeadBridge;

// Runs an action once when its popup starts closing, by any path, or when the popup is destroyed.
internal sealed class PopupCloseAction : MonoBehaviour
{
    private MenuPage _page = null!;
    private System.Action? _onClose;

    internal static void Attach(GameObject popup, MenuPage page, System.Action onClose)
    {
        var action = popup.AddComponent<PopupCloseAction>();
        action._page = page;
        action._onClose = onClose;
    }

    private void Update()
    {
        if (_page == null || _page.currentPageState == MenuPage.PageState.Closing) Run();
    }

    private void OnDestroy() => Run();

    private void Run()
    {
        var onClose = _onClose;
        _onClose = null;
        onClose?.Invoke();
    }
}
