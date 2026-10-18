using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private DeviceContext? deviceContext;
    private List<DeviceComponent> deviceComponents = [];
    private ListView? deviceList, secondaryDeviceList;
    private StackPanel? deviceDetails;
    private TextBlock? deviceCount;
    private Expander? secondaryDevices;
    private string deviceQuery = "", selectedDeviceKey = "";
    private int deviceScope;
    private DeviceCategory? deviceCategory;
    private bool loadingDevices, searchingDrivers;
    private List<UpdateOffer>? driverOffers;
    private string driverSearchError = "";
    private DateTimeOffset? driverSearchStarted, driverSearchFinished;
    private FrameworkElement Device()
    {
        deviceComponents = DevicePresentation.Build(devices ?? [], deviceContext ?? DeviceContext.Empty);
        var page = Ui.Stack(16);
        var intro = Ui.Stack(8);
        intro.Children.Add(Text(UiStrings.Get("Text069"), 20, true));
        intro.Children.Add(Hint(UiStrings.Get("Text070"), 13));
        if (deviceContext?.Computer is { } computer)
        {
            var board = Ui.Stack(8);
            board.Children.Add(Hint(UiStrings.Format("Text071", DriverSources.Brand(computer.BoardManufacturer) ?? DevicePresentation.Known(computer.BoardManufacturer), DevicePresentation.Known(computer.BoardModel)), 12));
            foreach (var source in BoardSources()) board.Children.Add(Button(source.Name + UiStrings.Get("Text072"), () => OpenManufacturer(source)));
            intro.Children.Add(new Expander { Header = UiStrings.Get("Text073"), Content = board, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        page.Children.Add(intro);
        var scan = new Button { Content = searchingDrivers ? UiStrings.Get("Text074") : UiStrings.Get("Text075"), FontSize = 14 * appearance.TextScale, Background = Accent, Foreground = AccentText, CornerRadius = new(ControlRadius), IsEnabled = !searchingDrivers && !loadingDevices };
        scan.Click += async (_, _) => await Guard(SearchDriverUpdates);
        var refresh = new Button { Content = UiStrings.Get("Text076"), FontSize = 14 * appearance.TextScale, CornerRadius = new(ControlRadius), IsEnabled = !loadingDevices && !searchingDrivers };
        refresh.Click += async (_, _) => await Guard(RefreshDevices);
        page.Children.Add(Ui.Row(scan, refresh));
        page.Children.Add(DriverSearchPanel());
        var search = new TextBox { PlaceholderText = UiStrings.Get("Text077"), Text = deviceQuery, FontSize = 14 * appearance.TextScale, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, UiStrings.Get("Text078"));
        var scope = new ComboBox { ItemsSource = new[] { UiStrings.Get("Text079"), UiStrings.Get("Text080"), UiStrings.Get("Text081") }, SelectedIndex = deviceScope, FontSize = 14 * appearance.TextScale, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scope, UiStrings.Get("Text082"));
        var categories = new ComboBox { ItemsSource = new[] { UiStrings.Get("Text083") }.Concat(Enum.GetValues<DeviceCategory>().Select(DevicePresentation.CategoryName)).ToArray(), SelectedIndex = deviceCategory is null ? 0 : (int)deviceCategory + 1, FontSize = 14 * appearance.TextScale, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categories, UiStrings.Get("Text084"));
        search.TextChanged += (_, _) => { deviceQuery = search.Text; FillDevices(); };
        scope.SelectionChanged += (_, _) => { if (scope.SelectedIndex >= 0) { deviceScope = scope.SelectedIndex; FillDevices(); } };
        categories.SelectionChanged += (_, _) => { if (categories.SelectedIndex >= 0) { deviceCategory = categories.SelectedIndex == 0 ? null : (DeviceCategory)(categories.SelectedIndex - 1); FillDevices(); } };
        page.Children.Add(Ui.Columns(search, Ui.Columns(scope, categories, 1, 1), 1, 1.6));
        deviceCount = Hint(""); page.Children.Add(deviceCount);
        deviceList = ComponentList(350); secondaryDeviceList = ComponentList(280);
        deviceDetails = Ui.Stack(9);
        var detailScroll = new ScrollViewer { Content = deviceDetails, Height = 318, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        page.Children.Add(Ui.Columns(Group(deviceList, new Thickness(6)), Group(detailScroll, new Thickness(22)), 1.45, 1));
        secondaryDevices = new Expander { Content = secondaryDeviceList, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        page.Children.Add(secondaryDevices);
        FillDevices();
        if (deviceContext?.Warnings.Count > 0) page.Children.Add(Hint(UiStrings.Get("Text085")));
        if ((devices is null || deviceContext is null) && !loadingDevices)
            DispatcherQueue.TryEnqueue(async () => await Guard(RefreshDevices));
        return page;
    }
    private ListView ComponentList(double height)
    {
        var list = new ListView { Height = height, ItemTemplate = (DataTemplate)Application.Current.Resources["DeviceComponentTemplate"], HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = ListViewSelectionMode.Single };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, height == 350 ? UiStrings.Get("Text086") : UiStrings.Get("Text062"));
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not DeviceComponent component) return;
            selectedDeviceKey = component.Key;
            var other = ReferenceEquals(list, deviceList) ? secondaryDeviceList : deviceList;
            if (other is not null) other.SelectedItem = null;
            ShowDeviceDetails(component);
        };
        return list;
    }
    private void FillDevices()
    {
        if (deviceList is null || secondaryDeviceList is null || secondaryDevices is null) return;
        var primary = DevicePresentation.Filter(deviceComponents, deviceQuery, deviceCategory, deviceScope).ToList();
        var secondary = deviceComponents.Where(c => c.Secondary && !c.NeedsAttention && (deviceCategory is null || c.Category == deviceCategory) && c.Matches(deviceQuery)).ToList();
        deviceList.ItemsSource = primary; secondaryDeviceList.ItemsSource = secondary;
        secondaryDevices.Header = UiStrings.Format("Text087", secondary.Count);
        secondaryDevices.Visibility = deviceScope == 0 && secondary.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (deviceCount is not null) deviceCount.Text = loadingDevices ? UiStrings.Get("Text088") : UiStrings.Format("Text089", primary.Count, deviceComponents.Count(d => d.NeedsAttention)) + (primary.Count == 0 ? UiStrings.Get("Text090") : "");
        var selected = primary.FirstOrDefault(c => c.Key.Equals(selectedDeviceKey, StringComparison.OrdinalIgnoreCase)) ?? primary.FirstOrDefault();
        if (selected is not null) deviceList.SelectedItem = selected;
        else
        {
            deviceDetails?.Children.Clear(); deviceDetails?.Children.Add(Hint(UiStrings.Get("Text091")));
            if (!string.IsNullOrWhiteSpace(deviceQuery) && secondary.Count > 0) secondaryDevices.IsExpanded = true;
        }
    }
    private void ShowDeviceDetails(DeviceComponent component)
    {
        if (deviceDetails is null) return;
        deviceDetails.Children.Clear(); deviceDetails.Children.Add(Text(component.Name, 18, true)); deviceDetails.Children.Add(Hint(component.Caption));
        if (component.ProcessorGroup)
        {
            var explanation = Ui.Stack(8);
            explanation.Children.Add(Hint(UiStrings.Format("Text092", component.Members.Count, component.Members.Count)));
            explanation.Children.Add(Hint(UiStrings.Get("Text093")));
            deviceDetails.Children.Add(new Expander { Header = UiStrings.Get("Text094"), Content = explanation, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        var facts = Ui.Stack(3);
        facts.Children.Add(Text(component.Status, 14, true));
        facts.Children.Add(Hint(UiStrings.Get("Text095") + component.Version));
        if (component.Recognition is { } recognition)
        {
            if (recognition.Vendor is not null) facts.Children.Add(Hint(UiStrings.Get("Text096") + recognition.Vendor));
            if (recognition.Storage is { } disk)
            {
                facts.Children.Add(Hint(UiStrings.Get("Text097") + DevicePresentation.Known(disk.Firmware)));
            }
        }
        deviceDetails.Children.Add(facts);
        if (component.NeedsAttention) deviceDetails.Children.Add(Hint(UiStrings.Get("Text098") + string.Join(", ", component.Members.Where(d => d.ProblemCode is > 0).Select(d => d.ProblemCode).Distinct())));
        var sources = component.Target is { } target && component.Recognition?.Nature is not DeviceNature.Software and not DeviceNature.WindowsComponent
            ? DriverUpdatePresentation.ManufacturerSources(target, null, component.Recognition?.Storage) : [];
        deviceDetails.Children.Add(Text(UiStrings.Get("Text099"), 15, true));
        if (sources.Count == 0) deviceDetails.Children.Add(Hint(component.ProcessorGroup ? UiStrings.Get("Text100")
            : component.Recognition?.Nature == DeviceNature.Software ? UiStrings.Get("Text101")
            : component.Recognition?.Nature == DeviceNature.WindowsComponent ? UiStrings.Get("Text102")
            : UiStrings.Get("Text103")));
        else
        {
            foreach (var source in sources)
            {
                var link = Button(source.Name == "Crucial Storage Executive" ? source.Name : source.Name + UiStrings.Get("Text104"), () => OpenManufacturer(source));
                link.Padding = new Thickness(12, 7, 12, 7); link.FontSize = 13 * appearance.TextScale;
                ToolTipService.SetToolTip(link, source.Url.IdnHost); deviceDetails.Children.Add(link);
            }
            deviceDetails.Children.Add(Hint(UiStrings.Get("Text105"), 12));
        }
        if (component.Recognition is { } evidence)
        {
            var explanation = Ui.Stack(8); explanation.Children.Add(Hint(evidence.Evidence));
            if (evidence.Storage is not null) explanation.Children.Add(Hint(UiStrings.Get("Text106"), 12));
            deviceDetails.Children.Add(new Expander { Header = UiStrings.Get("Text107"), Content = explanation, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        deviceDetails.Children.Add(Button(UiStrings.Get("Text108"), () => DeviceDetailsDialog(component)));
    }
    private IReadOnlyList<DriverSource> BoardSources()
    {
        if (deviceContext?.Computer is not { } pc) return [];
        var brands = new[] { pc.Manufacturer, pc.BoardManufacturer }.Select(DriverSources.Brand).Where(b => b is not null).ToHashSet();
        return DriverSources.Catalog.Where(s => brands.Contains(s.Name)).ToList();
    }
    private static async Task OpenManufacturer(DriverSource source)
    {
        if (!DriverSources.Trusted(source.Url) || source.Url.IdnHost.Equals("support.microsoft.com", StringComparison.OrdinalIgnoreCase) || source.Name == "Microsoft Update Catalog") throw new InvalidDataException(UiStrings.Get("Text109"));
        if (!await Windows.System.Launcher.LaunchUriAsync(source.Url)) throw new InvalidOperationException(UiStrings.Get("Text110"));
    }
    private async Task DeviceDetailsDialog(DeviceComponent component)
    {
        var body = Ui.Stack(16);
        foreach (var row in component.Members)
        {
            var item = Ui.Stack(5); item.Children.Add(Text(row.Name, 14, true));
            item.Children.Add(Selectable(UiStrings.Get("Text111") + row.InstanceId));
            item.Children.Add(Selectable(UiStrings.Get("Text112") + (row.HardwareIds.Length == 0 ? UiStrings.Get("Text113") : string.Join("\n", row.HardwareIds))));
            item.Children.Add(Selectable(UiStrings.Format("Text115", DevicePresentation.Known(row.Provider), DevicePresentation.Known(row.Version), DevicePresentation.Known(row.Date), DevicePresentation.Known(row.Inf), row.ProblemCode?.ToString() ?? UiStrings.Get("Text114"))));
            body.Children.Add(item);
        }
        if (component.Recognition is { } recognition)
        {
            body.Children.Add(Selectable(UiStrings.Get("Text116") + recognition.Evidence));
            if (recognition.Storage is { } disk) body.Children.Add(Selectable(UiStrings.Format("Text117", disk.Model, DevicePresentation.Known(disk.Firmware), disk.SizeBytes)));
        }
        await ShowDialog(new ContentDialog { Title = component.ProcessorGroup ? UiStrings.Get("Text118") : UiStrings.Get("Text108"), Content = new ScrollViewer { Content = body, MaxHeight = 360 }, CloseButtonText = UiStrings.Get("Text119"), XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme });
    }
    private TextBlock Selectable(string value) { var text = Hint(value); text.IsTextSelectionEnabled = true; return text; }
    private async Task RefreshDevices()
    {
        if (loadingDevices || searchingDrivers || closing) return;
        loadingDevices = true; if (currentPage == "Device") ShowPage("Device", true);
        try
        {
            var snapshot = await Task.Run(() => (Rows: DeviceInventory.Read(), Context: DeviceContextReader.Read()));
            if (closing) return;
            devices = snapshot.Rows; deviceContext = snapshot.Context; driverOffers = null; driverSearchError = ""; driverSearchFinished = null;
            foreach (var warning in deviceContext.Warnings) new AuditLog(dataDirectory).Write(warning);
        }
        finally { loadingDevices = false; if (!closing && currentPage == "Device") ShowPage("Device", true); }
    }
}
