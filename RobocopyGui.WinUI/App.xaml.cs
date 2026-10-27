// SPDX-FileCopyrightText: 2026 J1mmyHuang
// SPDX-License-Identifier: AGPL-3.0-or-later WITH AdditionRef-RobocopyGUI-Microsoft-Runtime-Exception

using Microsoft.UI.Xaml;

namespace RobocopyGui.WinUI
{
    public partial class App : Application
    {
        Window window;

        public App() { InitializeComponent(); }

        protected override void OnLaunched(LaunchActivatedEventArgs args) { window = new MainWindow(); window.Activate(); }
    }
}
