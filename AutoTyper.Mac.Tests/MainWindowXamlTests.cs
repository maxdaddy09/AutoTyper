using System.Xml.Linq;

namespace AutoTyper.Mac.Tests;

// The permission button is hidden unless permission is missing, which never happens in preview, and
// UI Automation does not publish hidden controls, so scripts/preview-check.ps1 cannot see it. These
// tests read the window's AXAML itself instead.
public class MainWindowXamlTests
{
    static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Walks up from the test's output folder to the repository folder that holds AutoTyper.Mac.
    static string FindMainWindowXaml()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "AutoTyper.Mac", "MainWindow.axaml");
            if (File.Exists(candidate)) return candidate;
        }

        Assert.Fail(
            $"AutoTyper.Mac/MainWindow.axaml was not found in any folder above {AppContext.BaseDirectory}");
        return "";
    }

    static XElement PermissionButton()
    {
        var path = FindMainWindowXaml();
        var named = XDocument.Load(path)
            .Descendants()
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "permissionButton")
            .ToList();

        Assert.True(named.Count == 1, $"expected exactly one element with x:Name=\"permissionButton\" in {path}, found {named.Count}");
        return named[0];
    }

    [Fact]
    public void PermissionButton_HasTheAutomationIdTheChecksLookFor()
    {
        Assert.Equal("permissionButton", (string?)PermissionButton().Attribute("AutomationProperties.AutomationId"));
    }

    [Fact]
    public void PermissionButton_IsHiddenUntilTheViewModelShowsIt()
    {
        Assert.Equal("False", (string?)PermissionButton().Attribute("IsVisible"));
    }

    [Fact]
    public void PermissionButton_OffersToOpenTheAccessibilitySettings()
    {
        Assert.Equal("Open Accessibility settings", (string?)PermissionButton().Attribute("Content"));
    }
}
