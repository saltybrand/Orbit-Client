using OrbitClient.Scripts;

namespace OrbitClient;

public partial class MainWindow
{
    private OrbitContentService orbitContent = null!;
    private void InitializeOrbitNetworking()
    {
        ConfigureOrbitNetworking();
        Closed += (_, _) => orbitContent.Dispose();
    }
    private void ConfigureOrbitNetworking()
    {
        orbitContent?.Dispose();
        var config = orbitConfig;
        try { config.Validate(); }
        catch (System.IO.IOException) { config = new OrbitAccountConfig(); }
        orbitContent = new(config, workspace.DataRoot);
    }
}
