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
        GameHeading.Text = running ? "NOW PLAYING" : "READY TO PLAY";
        GameDescription.Text = running ? "Gorilla Tag is open. You can stop playing here." : "Launch Gorilla Tag with Orbit.";
        LaunchTitle.Text = running ? "STOP PLAYING" : "LAUNCH GORILLA TAG";
        LaunchStatus.Text = running ? "GORILLA TAG IS RUNNING" : "READY TO LAUNCH";
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
