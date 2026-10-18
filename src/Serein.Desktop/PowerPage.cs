using System.Windows;
using System.Windows.Controls;
using Serein.Core;

namespace Serein.Desktop;
public partial class MainWindow
{
    private PowerLease? powerLease;
    private FrameworkElement Optimize()
    {
        var work = new StackPanel(); var techniques = new Grid { Margin = new(0, 0, 0, 16) };
        techniques.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); techniques.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var scheduling = new StackPanel(); scheduling.Children.Add(SectionTitle(T("Приоритет фоновых приложений", "Background app priority"), T("Временно снижает конкуренцию за CPU. Эффект зависит от фоновой нагрузки.", "Temporarily reduces CPU competition. Effects depend on background workload.")));
        scheduling.Children.Add(Muted(T("Исходный приоритет восстанавливается автоматически.", "Original priority is restored automatically."), 11, 16)); scheduling.Children.Add(Action(T("Выбрать приложения", "Choose apps"), () => SelectPage("Processes"))); techniques.Children.Add(Card(scheduling, new(0, 0, 16, 0)));
        var extreme = new StackPanel(); extreme.Children.Add(SectionTitle("Extreme Gaming", T("Приостанавливает выбранные приложения на время игры и восстанавливает их после выхода.", "Pauses chosen apps while playing and restores them when the game closes.")));
        extreme.Children.Add(Muted(T("Заморозка может удерживать общие ресурсы.", "Suspension can hold shared resources."), 11, 16)); extreme.Children.Add(Action(T("Настроить сессию", "Configure session"), () => SelectPage("Gaming"))); var extremeCard = Card(extreme); Grid.SetColumn(extremeCard, 1); techniques.Children.Add(extremeCard); work.Children.Add(techniques);
        work.Children.Add(Card(PowerControls(), new(0, 0, 0, 16)));
        return Scroll(work);
    }
    private FrameworkElement PowerControls()
    {
        var panel = new StackPanel(); panel.Children.Add(SectionTitle(T("Временная схема питания", "Temporary power scheme"), T("Выберите установленную схему. Исходная восстановится по окончании срока или сессии.", "Choose an installed scheme. The original is restored after the deadline or session.")));
        try
        {
            var platform = new WindowsPowerPlatform(); var schemes = platform.Schemes(); Guid active = platform.Current();
            var choice = new ComboBox { ItemsSource = schemes, DisplayMemberPath = "Name", SelectedItem = schemes.FirstOrDefault(p => p.Id == active), MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 12) };
            panel.Children.Add(Muted(T($"Сейчас: {schemes.FirstOrDefault(p => p.Id == active)?.Name ?? active.ToString()} · срок {selectedProfile.DeadlineSeconds} с", $"Current: {schemes.FirstOrDefault(p => p.Id == active)?.Name ?? active.ToString()} · {selectedProfile.DeadlineSeconds}s deadline"), 12));
            var selection = Row(); selection.Children.Add(choice);
            selection.Children.Add(Action(T("Применить временно…", "Apply temporarily…"), async () =>
            {
                if (DriverOperationActive) throw new InvalidOperationException(T("Дождитесь завершения установки драйвера.", "Wait for driver installation to finish."));
                if (choice.SelectedItem is not PowerScheme scheme) return;
                if (!Confirm(T($"Включить схему «{scheme.Name}» на {selectedProfile.DeadlineSeconds} с?\n\nОна применяется ко всему компьютеру. Прежняя схема восстановится автоматически, если за это время вы не выберете другую. На ноутбуке расход батареи может увеличиться.", $"Use “{scheme.Name}” for {selectedProfile.DeadlineSeconds}s?\n\nThis applies to the whole computer. Your previous plan is restored automatically unless you choose another in the meantime. Battery use may increase on laptops."))) return;
                await Send(new("Power", Seconds: selectedProfile.DeadlineSeconds, PowerScheme: scheme.Id)); ShowPage("Optimize");
            }));
            selection.Children.Add(Action(T("Восстановить", "Restore"), async () => { await RestoreAll(); ShowPage("Optimize"); })); panel.Children.Add(selection);
            var profileActions = Row();
            profileActions.Children.Add(Action(T("Сохранить в профиль", "Save to profile"), () =>
            {
                if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first."));
                if (choice.SelectedItem is PowerScheme scheme) { ReplaceProfile(selectedProfile with { PowerScheme = scheme.Id }); Report(T("Схема сохранена в профиль. Питание не изменялось.", "Scheme saved to profile. No power change applied.")); ShowPage("Optimize"); }
            }));
            profileActions.Children.Add(Action(T("Убрать из профиля", "Remove from profile"), () =>
            {
                if (armed) throw new InvalidOperationException(T("Сначала выключите автоматический режим.", "Turn off automatic management first.")); ReplaceProfile(selectedProfile with { PowerScheme = null }); Report(T("Схема удалена из профиля.", "Profile power option removed.")); ShowPage("Optimize");
            }));
            panel.Children.Add(profileActions);
            string saved = schemes.FirstOrDefault(p => p.Id == selectedProfile.PowerScheme)?.Name ?? T("не выбрана", "none");
            panel.Children.Add(Muted(T($"В профиле: {saved}. Изменение выбора само по себе ничего не применяет.", $"Saved in profile: {saved}. Changing the selection alone applies nothing."), 11, 0));
        }
        catch (Exception ex) { panel.Children.Add(Text(T("Управление недоступно: ", "Unavailable: ") + ex.Message)); }
        return panel;
    }
    private async Task ApplyGameProfile()
    {
        try
        {
            if (selectedProfile.PowerScheme is Guid scheme) await Send(new("Power", Seconds: selectedProfile.DeadlineSeconds, PowerScheme: scheme));
            foreach (var approval in selectedProfile.AllowedApps)
            foreach (var target in processRows.Where(r => r.Path.Equals(approval.Path, StringComparison.OrdinalIgnoreCase)))
                await Send(new(selectedProfile.Mode == ProfileMode.ExtremeGaming ? "Freeze" : "Priority", target.Identity, approval, selectedProfile.DeadlineSeconds, tracker.Roots.ToArray()));
        }
        catch { await RestoreAll(); throw; }
    }
}
