using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RobocopyGui.WinUI
{
    public enum StepState { Pending, Active, Done, Skipped, Stopped, Failed }

    // One row of the task-progress pane. It only reflects workflow state; it is not clickable navigation.
    public sealed partial class StepView : UserControl
    {
        public StepView() { InitializeComponent(); }

        public void Set(int number, string title, string description, StepState state, bool showConnector)
        {
            NumberText.Text = number.ToString(); TitleText.Text = title; DescriptionText.Text = description;
            Connector.Visibility = showConnector ? Visibility.Visible : Visibility.Collapsed;
            VisualStateManager.GoToState(this, state.ToString(), false);
        }
    }
}
