using System.Globalization;
using System.Windows.Data;

namespace Serein.Desktop;
internal sealed class DisplayTextConverter(bool english) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool enabled) return english ? (enabled ? "Yes" : "No") : (enabled ? "Да" : "Нет");
        if (value is DateTimeOffset date) return date.ToLocalTime().ToString("g", culture);
        if (value is DateTime time) return time.ToLocalTime().ToString("g", culture);
        if (parameter is not string field || field is not ("Classification" or "Priority" or "State" or "Status" or "StartMode" or "Health")) return value ?? "—";
        string? text = value?.ToString();
        return text switch
        {
            "CRITICAL" or "PROTECTED" => english ? "Protected" : "Защищено",
            "GAME_DEPENDENCY" => english ? "Used by game" : "Используется игрой",
            "UNKNOWN" => english ? "Not approved" : "Не разрешено",
            "ELIGIBLE_USER_APP" => english ? "Allowed" : "Разрешено",
            "BelowNormal" => english ? "Lower" : "Пониженный",
            "AboveNormal" => english ? "Higher" : "Повышенный",
            "Normal" => english ? "Normal" : "Обычный",
            "High" => english ? "High" : "Высокий",
            "Idle" => english ? "Minimum" : "Минимальный",
            "Running" => english ? "Running" : "Работает",
            "Stopped" => english ? "Stopped" : "Остановлено",
            "Auto" => english ? "Automatic" : "Автоматически",
            "Manual" => english ? "Manual" : "Вручную",
            "Disabled" => english ? "Disabled" : "Отключено",
            "OK" => english ? "OK" : "В порядке",
            "Unknown" => english ? "Unknown" : "Неизвестно",
            "Missing driver (28)" => english ? "Driver missing" : "Нет драйвера",
            "Applied" => english ? "Applied" : "Применено",
            "Reverted" => english ? "Restored" : "Восстановлено",
            "Attention" => english ? "Needs attention" : "Нужна проверка",
            _ => value ?? "—"
        };
    }
    public static string Header(string text, bool english) => english ? text : text switch
    {
        "Name" => "Название", "Path" or "Approved EXE" => "Приложение", "Service" => "Служба", "State" or "Status" or "Health" => "Состояние",
        "Start" => "Запуск", "Account" => "Учётная запись", "Enabled" => "Включено", "Origin" => "Источник", "Command" => "Команда",
        "Provider" => "Производитель", "Version" => "Версия", "Signed metadata" => "Подпись", "Hardware IDs" or "Hardware ID" => "Идентификаторы",
        "Title" => "Обновление", "Class" => "Тип", "Revision" => "Редакция", "Match" => "Совместимость", "Device" => "Устройство", "Instance" => "Идентификатор", "Windows rank" => "Приоритет Windows",
        "Bytes" => "Байт", "MiB" => "МБ", "Last write UTC" or "Modified UTC" => "Изменён", "PID" => "ID процесса", "TID" => "ID потока", "Action" => "Операция", "Details" => "Подробности", "Deadline UTC" => "Восстановить до",
        "Frames" => "Кадры", "Average FPS" => "Средний FPS", "Swapchain" => "Поток кадров", _ => text
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
