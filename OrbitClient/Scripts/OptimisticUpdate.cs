namespace OrbitClient.Scripts;

public static class OptimisticUpdate
{
    public static async Task ApplyAsync(Action update, Func<Task> save, Action rollback)
    {
        update();
        try { await save(); }
        catch { rollback(); throw; }
    }
}
