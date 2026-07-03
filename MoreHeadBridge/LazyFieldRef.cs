using HarmonyLib;

namespace MoreHeadBridge;

// Lazy FieldRefAccess with failure caching: resolved on first use (a static initializer would turn a
// missing field into a TypeLoadException that aborts patching); a miss (game update) warns once and
// degrades every later call to a no-op.
internal sealed class LazyFieldRef<TObj, TField> where TObj : class
{
    private readonly string _fieldName;
    private readonly string _feature;   // named in the "disabled" warning
    private AccessTools.FieldRef<TObj, TField>? _ref;

    internal bool Broken { get; private set; }

    internal LazyFieldRef(string fieldName, string feature)
    {
        _fieldName = fieldName;
        _feature = feature;
    }

    internal bool TryResolve()
    {
        if (Broken) return false;
        if (_ref != null) return true;
        try
        {
            _ref = AccessTools.FieldRefAccess<TObj, TField>(_fieldName);
            return true;
        }
        catch (System.Exception ex)
        {
            Broken = true;
            BceConsole.LogWarning($"{typeof(TObj).Name}.{_fieldName} not found (game update?) — {_feature} disabled: {ex.Message}");
            return false;
        }
    }

    internal bool TryGet(TObj instance, out TField value)
    {
        if (!TryResolve()) { value = default!; return false; }
        value = _ref!(instance);
        return true;
    }

    internal bool TrySet(TObj instance, TField value)
    {
        if (!TryResolve()) return false;
        _ref!(instance) = value;
        return true;
    }
}
