using OrbitClient.Scripts;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace OrbitClient;

public partial class MainWindow
{
    private readonly DispatcherTimer gameStateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool gameActionInProgress;

    private void InitializeGameState()
    {
        gameStateTimer.Tick += (_, _) => RefreshGameControls();
        RefreshGameControls();
        gameStateTimer.Start();
        Closed += (_, _) => gameStateTimer.Stop();
    }

    private void RefreshGameControls() => ApplyGameState(WorkspaceService.IsGameRunning());

    private void ApplyGameState(bool running)
    {
        IsGtagRunning = running;
        if (gameActionInProgress) return;
        GameHeading.Text = "Gorilla Tag";
        GameDescription.Text = running ? "Gorilla Tag is open. You can stop playing here." : "Your game, mods, and next session in one place.";
        LaunchTitle.Text = running ? "Stop playing" : "Launch Gorilla Tag";
        LaunchStatus.Visibility = Visibility.Collapsed;
        LaunchButton.ToolTip = running ? "Close Gorilla Tag" : "Launch Gorilla Tag through Steam";
        LaunchButton.Style = (Style)FindResource(running ? "StopButton" : "Primary");
        LaunchButton.IsEnabled = fileOperation.CurrentCount > 0;
    }

    private async Task<bool> WaitForGameStateAsync(bool running, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!windowClosed && watch.Elapsed < timeout)
        {
            if (WorkspaceService.IsGameRunning() == running) return true;
            await Task.Delay(500);
        }
        return false;
    }
}
