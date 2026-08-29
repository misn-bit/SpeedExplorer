using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SpeedExplorer;

internal sealed class HotkeyBindingMap
{
    private readonly Dictionary<string, Keys> _actionToKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Keys, string> _keysToAction = new();

    public void Load(IEnumerable<KeyValuePair<string, string>> shortcuts)
    {
        _actionToKeys.Clear();
        _keysToAction.Clear();

        var converter = new KeysConverter();
        foreach (var kvp in shortcuts)
        {
            try
            {
                if (converter.ConvertFromString(kvp.Value) is not Keys parsed)
                    continue;

                var normalized = NormalizeBinding(parsed);
                _actionToKeys[kvp.Key] = normalized;

                // If multiple actions share the same key, keep the first one (stable).
                if (!_keysToAction.ContainsKey(normalized))
                    _keysToAction[normalized] = kvp.Key;
            }
            catch (ArgumentException)
            {
                // Ignore malformed entries from the settings file.
            }
            catch (FormatException)
            {
                // Ignore malformed entries from the settings file.
            }
            catch (NotSupportedException)
            {
                // Ignore key names that this platform cannot convert.
            }
        }
    }

    public bool TryGetBinding(string action, out Keys keys)
        => _actionToKeys.TryGetValue(action, out keys);

    public bool IsActionKeyCode(string action, Keys keyCode)
    {
        if (!_actionToKeys.TryGetValue(action, out var keys))
            return false;
        return (keys & Keys.KeyCode) == keyCode;
    }

    public bool IsActionKeyData(string action, Keys keyData)
    {
        if (!_actionToKeys.TryGetValue(action, out var keys))
            return false;
        return NormalizeBinding(keyData) == keys;
    }

    public bool TryGetAction(Keys effectiveKeyData, out string action)
        => _keysToAction.TryGetValue(effectiveKeyData, out action!);

    private static Keys NormalizeBinding(Keys binding)
    {
        var code = binding & Keys.KeyCode;
        var mods = binding & (Keys.Control | Keys.Shift | Keys.Alt);
        return code | mods;
    }
}
