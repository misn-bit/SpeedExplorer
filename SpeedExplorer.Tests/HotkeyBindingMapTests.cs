using System.Collections.Generic;
using System.Windows.Forms;

namespace SpeedExplorer.Tests;

public sealed class HotkeyBindingMapTests
{
    [Fact]
    public void Load_NormalizesBindingsAndMapsActions()
    {
        var map = new HotkeyBindingMap();
        map.Load(new Dictionary<string, string>
        {
            ["Open"] = "Control, O",
            ["Duplicate"] = "Control, O",
            ["Escape"] = "Escape"
        });

        Assert.True(map.TryGetBinding("Open", out var open));
        Assert.Equal(Keys.Control | Keys.O, open);
        Assert.True(map.IsActionKeyCode("Open", Keys.O));
        Assert.True(map.IsActionKeyData("Open", Keys.Control | Keys.O));
        Assert.True(map.TryGetAction(Keys.Control | Keys.O, out var action));
        Assert.Equal("Open", action);
    }

    [Fact]
    public void Load_IgnoresMalformedEntriesWithoutDiscardingValidEntries()
    {
        var map = new HotkeyBindingMap();
        map.Load(new Dictionary<string, string>
        {
            ["Broken"] = "NotARealKeyName",
            ["Valid"] = "F5"
        });

        Assert.False(map.TryGetBinding("Broken", out _));
        Assert.True(map.TryGetBinding("Valid", out var valid));
        Assert.Equal(Keys.F5, valid);
    }

    [Fact]
    public void Load_ReplacesPreviousBindings()
    {
        var map = new HotkeyBindingMap();
        map.Load(new Dictionary<string, string> { ["Old"] = "F5" });
        map.Load(new Dictionary<string, string> { ["New"] = "F6" });

        Assert.False(map.TryGetBinding("Old", out _));
        Assert.True(map.TryGetAction(Keys.F6, out var action));
        Assert.Equal("New", action);
    }
}
