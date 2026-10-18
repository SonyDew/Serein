using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serein.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Serein.Desktop;
public partial class MainWindow
{
    private void VerifyBackgroundSelector(string directory, List<string> results)
    {
        var fixture = new Approval(@"G:\Serein test fixture\Saved app.exe", new string('A', 64));
        bool saved = false;
        var dialog = CreateBackgroundAppsDialog([new(fixture.Path, "Saved app", fixture), new(@"G:\Serein test fixture\Optional app.exe", "Optional app", null)], _ => { saved = true; return Task.CompletedTask; });
        var content = (FrameworkElement)dialog.Content; dialog.Content = null;
        var root = new Border { Background = (Brush)FindResource("CanvasBrush"), Child = content, Width = 650, Height = 580 };
        root.Measure(new Size(650, 580)); root.Arrange(new Rect(0, 0, 650, 580)); root.UpdateLayout();
        var boxes = Descendants(root).OfType<CheckBox>().ToList(); var search = Descendants(root).OfType<TextBox>().Single();
        if (boxes.Count != 2 || boxes.Count(b => b.IsChecked == true) != 1 || dialog.ResizeMode != ResizeMode.NoResize || !Descendants(root).OfType<Button>().Any(b => b.IsCancel && b.IsDefault)) throw new InvalidOperationException("Background selector lost saved-only selection or cancellation/default sizing rules.");
        boxes.Single(b => b.IsChecked != true).IsChecked = true;
        search.Text = "Optional";
        if (boxes.Count(b => b.Visibility == Visibility.Visible) != 1 || boxes.Count(b => b.IsChecked == true) != 2 || saved) throw new InvalidOperationException("Filtering must preserve selections without writing approvals or applying actions.");
        search.Text = ""; root.UpdateLayout();
        var image = new RenderTargetBitmap(650, 580, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(directory, "Background-apps.png")); png.Save(stream);
        dialog.Close(); results.Add("Background-app selector rendered; saved-only defaults, checkbox selection, filtering without loss, default Cancel and fixed size verified; no approvals/actions written.");
    }
}
