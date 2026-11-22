using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace PokerCaption;

internal static class UiLanguage
{
    private sealed record Caption(string Text, string AccessibleName, string AccessibleDescription);
    private static readonly ConditionalWeakTable<Control, Caption> English = new();
    private static readonly ResourceManager Resources = new("PokerCaption.UiStrings", typeof(UiLanguage).Assembly);
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    internal static bool IsItalian => _culture.Name == "it";

    internal static void Use(string language) =>
        _culture = CultureInfo.GetCultureInfo(language == "it" ? "it" : "en");

    internal static string Text(string english) =>
        string.IsNullOrEmpty(english) || !IsItalian ? english : Resources.GetString(english, _culture) ?? english;

    internal static void Apply(Control root)
    {
        foreach (var control in DesktopWindow.Descendants(root).Prepend(root))
        {
            if (control is not (Label or ButtonBase or GroupBox or TabPage)) continue;
            var caption = English.GetValue(control, value => new(value.Text, value.AccessibleName, value.AccessibleDescription));
            control.Text = Text(caption.Text);
            control.AccessibleName = Text(caption.AccessibleName);
            control.AccessibleDescription = Text(caption.AccessibleDescription);
        }
    }
}
