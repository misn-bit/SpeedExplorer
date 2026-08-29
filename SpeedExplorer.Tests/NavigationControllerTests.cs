using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SpeedExplorer.Tests;

public sealed class NavigationControllerTests
{
    [Fact]
    public void GoBack_MovesCurrentPathToForwardHistoryAndNavigatesToPreviousPath()
    {
        var host = new FakeNavigationHost { CurrentPath = @"C:\current" };
        var controller = new NavigationController(host);
        controller.BackHistory.Push(@"C:\previous");

        controller.GoBack();

        Assert.Equal(@"C:\previous", host.CurrentPath);
        Assert.Equal(@"C:\current", Assert.Single(controller.ForwardHistory));
        Assert.Empty(controller.BackHistory);
        Assert.Contains("NavController.GoBack", host.ObservedSources);
    }

    [Fact]
    public void GoForward_MovesCurrentPathToBackHistoryAndNavigatesToNextPath()
    {
        var host = new FakeNavigationHost { CurrentPath = @"C:\current" };
        var controller = new NavigationController(host);
        controller.ForwardHistory.Push(@"C:\next");

        controller.GoForward();

        Assert.Equal(@"C:\next", host.CurrentPath);
        Assert.Equal(@"C:\current", Assert.Single(controller.BackHistory));
        Assert.Empty(controller.ForwardHistory);
        Assert.Contains("NavController.GoForward", host.ObservedSources);
    }

    [Fact]
    public void GoUp_UsesShellParentWhenCurrentPathIsShellPath()
    {
        var host = new FakeNavigationHost
        {
            CurrentPath = "shell:child",
            ShellParentPath = "shell:parent"
        };
        var controller = new NavigationController(host);

        controller.GoUp();

        Assert.Equal("shell:parent", host.CurrentPath);
        Assert.Contains("NavController.GoUpShell", host.ObservedSources);
    }

    [Fact]
    public void GoUp_UsesDirectoryParentForFilesystemPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SpeedExplorer.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string child = Path.Combine(directory, "child");
            Directory.CreateDirectory(child);
            var host = new FakeNavigationHost { CurrentPath = child };
            var controller = new NavigationController(host);

            controller.GoUp();

            Assert.Equal(directory, host.CurrentPath);
            Assert.Contains("NavController.GoUp", host.ObservedSources);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeNavigationHost : INavigationHost
    {
        public string CurrentPath { get; set; } = "";
        public string ThisPcPath => "::ThisPC";
        public string? ShellParentPath { get; set; }
        public List<string> ObservedSources { get; } = new();

        public bool IsShellPath(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

        public void ClearCurrentPathForHistory() => CurrentPath = "";

        public string? GetShellParentPath(string shellPath) => ShellParentPath;

        public Task NavigateTo(string path)
        {
            CurrentPath = path;
            return Task.CompletedTask;
        }

        public Task RefreshCurrentAsync(List<string>? selectPaths) => Task.CompletedTask;

        public void ObserveTask(Task task, string source)
        {
            task.GetAwaiter().GetResult();
            ObservedSources.Add(source);
        }
    }
}
