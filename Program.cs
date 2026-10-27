using System;
using System.Windows.Forms;

namespace RobocopyGui
{
    internal static class Program { [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); } }
}
