using System.Drawing;
using System.Windows.Forms;

namespace PokerCaption
{
    public sealed partial class MainUiController
    {
        private static void ShowBetFormulaHelp(Form owner)
        {
            using var dialog = new Form
            {
                Name = "BetFormulaGuide",
                Text = "BetScript formula guide",
                Icon = ProductBrand.Icon,
                BackColor = DesktopWindow.Canvas,
                ClientSize = new Size(690, 560),
                MinimumSize = new Size(440, 360),
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                MinimizeBox = false,
                MaximizeBox = false,
            };
            var guide = new RichTextBox
            {
                Name = "FormulaGuideText", AccessibleName = "Formula syntax and examples",
                Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = true,
                BorderStyle = BorderStyle.None, BackColor = SystemColors.Window,
                ForeColor = DesktopWindow.Ink, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            using var bodyFont = new Font("Consolas", 11);
            using var headingFont = new Font(bodyFont, FontStyle.Bold);
            guide.Font = bodyFont;
            guide.Text = """
                    OPERATORS
                    Use ^ for powers, * and / for multiplication and division,
                    and + and - for addition and subtraction. Use parentheses to
                    group terms. Add bb to a number to express it in big blinds.
                    Both 0.5 and 0,5 are accepted. Variable names ignore case.

                    TABLE VALUES
                    pot         Total pot, including bets already made
                    spot        Pot at the start of the street
                    ante       Ante per player
                    antes      Sum of all antes
                    bet         Current bet to match, before subtracting your own bet
                    raise      Most recent bet or raise
                    hbet       Your contribution on this street, including blinds
                    call        Amount you need to call
                    stack      Your remaining stack
                    effStack   Effective stack
                    betbox     Amount currently in the bet box
                    sb / bb    Small blind / big blind

                    EXAMPLES
                    3.5bb gives a size of 3.5 big blinds.
                    pot * 0.5 gives half the current pot. pot x 0.5 also works.
                    raise * 3 triples the last bet or raise.
                    pot + 1bb - antes can be used for an open raise.
                    effStack / 3 uses one third of the effective stack.
                    x1 calculates a pot-size bet or raise, accounting for the call.
                    x1 + 1bb adds one big blind to that size.
                    x(1 / 2) calculates a half-pot size with the same adjustment.

                    DISPLAY
                    Add @0 through @15 to set decimal places (default 2).
                    Add @H to leave the bet box untouched, for example pot@H.

                    Some table values may be unavailable on your client.
                    BetScript tries the next rule if a required value is missing.
                    """.ReplaceLineEndings("\r\n");
            foreach (var heading in new[] { "OPERATORS", "TABLE VALUES", "EXAMPLES", "DISPLAY" })
            {
                guide.Select(guide.Text.IndexOf(heading, StringComparison.Ordinal), heading.Length);
                guide.SelectionFont = headingFont;
                guide.SelectionColor = DesktopWindow.Accent;
            }
            guide.Select(0, 0);
            var body = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window, Padding = new Padding(18, 14, 12, 10) };
            body.Controls.Add(guide);
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 5, 8, 0),
                FlowDirection = FlowDirection.RightToLeft,
            };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(close);
            dialog.Controls.Add(body);
            dialog.Controls.Add(footer);
            dialog.CancelButton = close;
            dialog.ActiveControl = close;
            dialog.ShowDialog(owner);
        }
    }
}
