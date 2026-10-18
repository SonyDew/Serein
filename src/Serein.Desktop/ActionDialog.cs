using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace Serein.Desktop;
public partial class MainWindow
{
    private Window CreateActionDialog(string title, string message, bool confirmation)
    {
        var dialog = new Window { Title = "Serein · " + title, Width = System.Math.Min(590, SystemParameters.WorkArea.Width - 32), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (IsLoaded) dialog.Owner = this;
        var content = new StackPanel { Margin = new(26) };
        var heading = Text(title, 23, 12); heading.FontWeight = FontWeights.SemiBold; heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); content.Children.Add(heading);
        var body = Text(message, 13, 0); body.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        content.Children.Add(new ScrollViewer { Content = body, MaxHeight = System.Math.Max(100, System.Math.Min(360, SystemParameters.WorkArea.Height - 180)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 0, 10, 0), Margin = new(0, 0, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirmation)
        {
            var cancel = new Button { Content = T("Отмена", "Cancel"), IsCancel = true, IsDefault = true, MinWidth = 94 }; actions.Children.Add(cancel);
            var proceed = new Button { Content = T("Продолжить", "Continue"), MinWidth = 110, Style = (Style)FindResource("PrimaryButtonStyle") }; proceed.Click += (_, _) => dialog.DialogResult = true; actions.Children.Add(proceed);
        }
        else
        {
            var close = new Button { Content = T("Понятно", "Close"), IsCancel = true, IsDefault = true, Style = (Style)FindResource("PrimaryButtonStyle"), MinWidth = 100 }; actions.Children.Add(close);
        }
        content.Children.Add(actions); dialog.Content = content; return dialog;
    }
    private bool Confirm(string message) => CreateActionDialog(T("Подтвердите действие", "Confirm action"), message, true).ShowDialog() == true;
    private void ShowNotice(string message) => CreateActionDialog(T("Действие не выполнено", "Action was not completed"), message, false).ShowDialog();
}
