using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serein.Core;

namespace Serein_Gaming_WinUI;

public sealed partial class MainWindow
{
    private FrameworkElement DriverSearchPanel()
    {
        var body = Ui.Stack(10);
        if (searchingDrivers)
        {
            var status = Text(UiStrings.Get("Text127"), 14, true);
            liveUpdates.Add(() => status.Text = UiStrings.Get("Text128") + (DateTimeOffset.UtcNow - driverSearchStarted!.Value).ToString(@"mm\:ss"));
            body.Children.Add(Ui.Row(new ProgressRing { Width = 20, Height = 20, IsActive = true }, status));
            body.Children.Add(Hint(UiStrings.Get("Text129")));
        }
        else if (driverSearchError.Length > 0)
        {
            body.Children.Add(Text(UiStrings.Get("Text130"), 15, true)); body.Children.Add(Hint(driverSearchError));
        }
        else if (driverOffers is null)
            body.Children.Add(Hint(UiStrings.Get("Text131")));
        else
        {
            body.Children.Add(Text(driverOffers.Count == 0 ? UiStrings.Get("Text132") : UiStrings.Format("Text133", driverOffers.Count), 16, true));
            body.Children.Add(Hint(UiStrings.Format("Text134", driverSearchFinished?.ToLocalTime())));
            if (driverOffers.Count > 0)
            {
                var results = Ui.Stack(16);
                foreach (var offer in driverOffers)
                {
                    var item = Ui.Stack(5); item.Children.Add(Text(offer.DeviceName, 14, true)); item.Children.Add(Text(offer.Candidate.Title, 13));
                    item.Children.Add(Hint(offer.Installed)); item.Children.Add(Hint(offer.Association));
                    if (offer.DeviceNames.Count > 1)
                    {
                        var names = Ui.Stack(6); foreach (var name in offer.DeviceNames) names.Children.Add(Hint(name));
                        item.Children.Add(new Expander { Header = UiStrings.Get("Text135"), Content = names, HorizontalAlignment = HorizontalAlignment.Stretch });
                    }
                    results.Children.Add(item);
                }
                body.Children.Add(new ScrollViewer { Content = results, MaxHeight = 240, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
                body.Children.Add(Button(UiStrings.Get("Text136"), async () =>
                {
                    if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate-optionalupdates")))
                        if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate"))) throw new InvalidOperationException(UiStrings.Get("Text137"));
                }));
                body.Children.Add(Hint(UiStrings.Get("Text138")));
            }
            else body.Children.Add(Hint(UiStrings.Get("Text139")));
        }
        return Group(body, new Thickness(16));
    }
    private async Task SearchDriverUpdates()
    {
        if (searchingDrivers || loadingDevices || closing) return;
        if (devices is null || deviceContext is null) await RefreshDevices();
        if (devices is null || closing) return;
        var snapshot = devices.ToList(); searchingDrivers = true; driverOffers = null; driverSearchError = "";
        driverSearchStarted = DateTimeOffset.UtcNow; driverSearchFinished = null;
        if (currentPage == "Device") ShowPage("Device", true);
        try
        {
            // Read-only discovery. No Resolve/Download/Install or EULA acceptance.
            var found = await Task.Run(() => DriverRecommendations.SearchWindowsUpdate(snapshot));
            if (closing) return;
            driverOffers = DriverUpdatePresentation.Build(found, snapshot); driverSearchFinished = DateTimeOffset.UtcNow;
            new AuditLog(dataDirectory).Write($"Explicit Windows Update driver search: {found.Count} assigned, {driverOffers.Count} non-firmware offers.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            new AuditLog(dataDirectory).Write(ex.ToString());
            driverSearchError = UiStrings.Get("Text140");
        }
        finally { searchingDrivers = false; if (!closing && currentPage == "Device") ShowPage("Device", true); }
    }
}
