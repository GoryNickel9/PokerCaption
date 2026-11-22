using System.Windows.Forms;

namespace PokerCaption;

public sealed partial class MainUiController
{
    internal static void PrepareMockupPreview(Form form)
    {
        var sample = new Config.IniFile(string.Empty);
        sample.Set("AdvancedBets", "Count", 2);
        sample.Set("AdvancedBets", "Streets0", 1);
        sample.Set("AdvancedBets", "Formula0", "pot*2");
        sample.Set("AdvancedBets", "Streets1", 2);
        sample.Set("AdvancedBets", "Formula1", "pot*0.5");
        FillBetscriptList(form, sample);

        foreach (var (listName, fieldName, first, second) in new[]
        {
            ("tablePanelsList", "tablePanelsFormatStr", "[effSPR]  [totalPot_bb]", "[stack_bb]"),
            ("playerPanelsList", "playerPanelsFormatStr", "[nick]  [pos]", "[bet_bb]"),
            ("generalHudPanelsList", "generalHudFormatStr", "[sessionTime]", "[ntables]"),
        })
        {
            var list = (ListBox)Ctl(form, listName);
            var field = (TextBox)Ctl(form, fieldName);
            list.Items.AddRange(new object[] { first, second });
            list.SelectedIndexChanged += (_, _) => field.Text = list.SelectedItem as string ?? "";
            list.SelectedIndex = 0;
        }
    }
}
