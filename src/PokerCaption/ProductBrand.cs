using System.Drawing;

namespace PokerCaption;

internal static class ProductBrand
{
    internal static Icon Icon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(ProductBrand).Assembly.GetManifestResourceStream("PokerCaption.ProgramIcon");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
