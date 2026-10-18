using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private void ApplyShellLanguage()
    {
        Root.Language = UiStrings.Language;
        foreach (ListViewItem item in Navigation.Items)
        {
            string key = (string)item.Tag switch { "Gaming" => "Text201", "Processes" => "Text202", "Monitoring" => "Text203", "Device" => "Text342", "Settings" => "Text204", _ => "Text206" };
            ((StackPanel)item.Content).Children.OfType<TextBlock>().Single().Text = UiStrings.Get(key);
        }
        RestoreButton.Content = UiStrings.Get("Text223");
        ToolTipService.SetToolTip(ThemeButton, UiStrings.Get("Text343"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ThemeButton, UiStrings.Get("Text343"));
    }
    private void ChangeLanguage(string code)
    {
        var options = new LanguageOptions(code); options.Validate();
        JsonFiles.Write(System.IO.Path.Combine(dataDirectory, "language.json"), options);
        UiStrings.SetLanguage(code); processSort = "Memory";
        ApplyShellLanguage(); ApplyAppearance(); ShowPage(currentPage, true);
    }
    private FrameworkElement LanguageSettings()
    {
        var content = Ui.Stack(12);
        content.Children.Add(Choice(UiStrings.Get("AppLanguage"), ["English", "Русский", "Deutsch"], UiStrings.Language switch { "ru" => 1, "de" => 2, _ => 0 }, index => ChangeLanguage(new[] { "en", "ru", "de" }[index])));
        content.Children.Add(Hint(UiStrings.Get("LanguageHint")));
        content.Children.Add(Text(UiStrings.Get("BetaVersion"), 15, true));
        content.Children.Add(Hint(UiStrings.Get("BetaHint")));
        content.Children.Add(Button(UiStrings.Get("OpenProject"), async () =>
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/SonyDew/Serein"))) throw new InvalidOperationException(UiStrings.Get("Text110"));
        }));
        return Group(content);
    }
}
