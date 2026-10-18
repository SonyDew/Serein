using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Serein.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Serein.Desktop;
public partial class MainWindow
{
    private async Task ChooseBackgroundApps()
    {
        EnsureBackgroundEditing(); var original = selectedProfile;
        await ReadProcesses(); EnsureBackgroundEditing();
        var choices = BackgroundAppSelection.Candidates(processRows, original, WindowsIdentity.GetCurrent().User?.Value ?? "");
        var dialog = CreateBackgroundAppsDialog(choices, async selected =>
        {
            EnsureBackgroundEditing();
            var approvals = await Task.Run(() => BackgroundAppSelection.ApproveSelection(choices, selected));
            EnsureBackgroundEditing();
            if (!ReferenceEquals(selectedProfile, original)) throw new InvalidOperationException(T("Профиль изменился. Откройте выбор приложений заново.", "The profile changed. Reopen the app selector."));
            ReplaceProfile(original with { AllowedApps = approvals });
            Report(T("Фоновые приложения выбраны. Для применения включите автоматический режим.", "Background apps saved. Enable automatic management to apply changes."));
        });
        if (dialog.ShowDialog() == true) ShowPage("Gaming");
    }
    private void EnsureBackgroundEditing()
    {
        if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first."));
        if (DriverOperationActive) throw new InvalidOperationException(T("Дождитесь завершения установки драйвера.", "Wait for driver installation to finish."));
    }
    private Window CreateBackgroundAppsDialog(IReadOnlyList<BackgroundAppChoice> choices, Func<List<string>, Task> save)
    {
        var dialog = new Window { Title = T("Serein · Фоновые приложения", "Serein · Background apps"), Width = Math.Min(650, SystemParameters.WorkArea.Width - 32), Height = Math.Min(620, SystemParameters.WorkArea.Height - 32), ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (IsLoaded) dialog.Owner = this;
        var content = new DockPanel { Margin = new(24) }; dialog.Content = content;
        var top = new StackPanel(); top.Children.Add(SectionTitle(T("Что приостановить на время игры?", "What can pause while you play?"), T("Выберите только ненужные во время игры приложения. Заморозка приостановит их загрузки и сохранение файлов. Steam, Discord, игра и системные компоненты защищены.", "Choose only apps you do not need while playing. Freezing pauses their downloads and file saves. Steam, Discord, your game and system components stay protected.")));
        top.Children.Add(Muted(T("Сохранение списка ничего не замораживает. Изменения начнутся после включения автоматического режима и запуска игры.", "Saving this list does not freeze anything. Changes begin after you enable automatic management and start the game."), 11, 12));
        var search = new TextBox { Margin = new(0, 0, 0, 12), ToolTip = T("Поиск по названию", "Search by name") }; top.Children.Add(search); DockPanel.SetDock(top, Dock.Top); content.Children.Add(top);
        var actions = Row(); actions.HorizontalAlignment = HorizontalAlignment.Right; actions.Margin = new(0, 16, 0, 0);
        var cancel = new Button { Content = T("Отмена", "Cancel"), IsCancel = true, IsDefault = true }; actions.Children.Add(cancel);
        var apply = new Button { Content = T("Сохранить выбор", "Save selection"), Style = (Style)FindResource("PrimaryButtonStyle") }; actions.Children.Add(apply); DockPanel.SetDock(actions, Dock.Bottom); content.Children.Add(actions);
        var list = new StackPanel(); var boxes = new List<(BackgroundAppChoice Choice, CheckBox Box)>();
        foreach (var choice in choices)
        {
            var label = new StackPanel(); label.Children.Add(Text(choice.Name, 14, 2)); var path = Muted(choice.Path, 10, 0); path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap; label.Children.Add(path);
            var box = new CheckBox { Content = label, IsChecked = choice.Saved is not null, Margin = new(0, 7, 0, 7), ToolTip = choice.Path }; boxes.Add((choice, box)); list.Children.Add(box);
        }
        if (choices.Count == 0) list.Children.Add(Muted(T("Подходящих запущенных приложений нет. Откройте нужное приложение и вернитесь к выбору.", "No eligible apps are running. Open an app and return to this selector.")));
        search.TextChanged += (_, _) => { foreach (var entry in boxes) entry.Box.Visibility = (entry.Choice.Name + " " + entry.Choice.Path).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        content.Children.Add(Scroll(list));
        bool saving = false; dialog.Closing += (_, e) => { if (saving) e.Cancel = true; };
        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = cancel.IsEnabled = false; saving = true;
            try
            {
                var selected = boxes.Where(x => x.Box.IsChecked == true).Select(x => x.Choice.Path).ToList();
                if (selected.Count > 64) throw new InvalidOperationException(T("Выберите не более 64 приложений.", "Choose at most 64 apps."));
                await save(selected); saving = false; dialog.DialogResult = true;
            }
            catch (Exception ex) { saving = false; ShowNotice(ex.Message); apply.IsEnabled = cancel.IsEnabled = true; }
        };
        return dialog;
    }
}
