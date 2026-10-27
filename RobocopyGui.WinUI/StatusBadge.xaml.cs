using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RobocopyGui.WinUI
{
    public enum BadgeTone { Neutral, Attention, Accent, Success, Caution, Critical }

    public sealed partial class StatusBadge : UserControl
    {
        public StatusBadge() { InitializeComponent(); }

        public void Set(string glyph, string text, BadgeTone tone) { Icon.Glyph = glyph; Label.Text = text; VisualStateManager.GoToState(this, tone.ToString(), false); }
    }
}
