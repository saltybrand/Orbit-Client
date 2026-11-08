using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace OrbitClient.Scripts;

public static class SteamAccountManager
{
    private static readonly SemaphoreSlim AccountSwitch = new(1, 1);
    public static string SteamPath
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var path = key?.GetValue("SteamPath")?.ToString();
            return string.IsNullOrWhiteSpace(path) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") : Path.GetFullPath(path);
        }
    }
    public static string SteamExe => Path.Combine(SteamPath, "steam.exe");
    public static string LoginUsersFile => Path.Combine(SteamPath, "config", "loginusers.vdf");
    public static List<SteamAccount> GetAccounts() => File.Exists(LoginUsersFile) ? ParseAccounts(File.ReadAllText(LoginUsersFile)) : [];
    public static List<SteamAccount> ParseAccounts(string text)
    {
        var accounts = new Dictionary<string, SteamAccount>();
        foreach (Match block in Regex.Matches(text, "\"(?<id>[0-9]{17})\"\\s*\\{(?<fields>[^{}]*)\\}", RegexOptions.Singleline))
        {
            var account = new SteamAccount { SteamId = block.Groups["id"].Value };
            foreach (Match field in Regex.Matches(block.Groups["fields"].Value, "\"(?<key>[^\"]+)\"\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\""))
            {
                var value = field.Groups["value"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
                switch (field.Groups["key"].Value.ToLowerInvariant())
                {
                    case "accountname": account.AccountName = value; break;
                    case "personaname": account.PersonalName = value; break;
                }
            }
            if (!string.IsNullOrWhiteSpace(account.AccountName)) accounts.TryAdd(account.SteamId, account);
        }
        return accounts.Values.OrderBy(x => x.PersonalName.Length > 0 ? x.PersonalName : x.AccountName, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static SteamAccount? GetCurrentAccount()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var user = key?.GetValue("AutoLoginUser")?.ToString();
        return string.IsNullOrWhiteSpace(user) ? null : GetAccounts().FirstOrDefault(x => string.Equals(x.AccountName, user, StringComparison.OrdinalIgnoreCase));
    }
    public static async Task SwitchGtagAccount(SteamAccount account)
    {
        if (!await AccountSwitch.WaitAsync(0)) throw new IOException("An account switch is already running.");
        try
        {
            WorkspaceService.RequireGameClosed();
            if (!File.Exists(SteamExe)) throw new IOException("Steam was not found. Open Steam and try again.");
            if (!GetAccounts().Any(x => x.SteamId == account.SteamId && x.AccountName == account.AccountName))
                throw new IOException("This account is no longer saved in Steam. Refresh the list.");
            await CloseSteam();
            WorkspaceService.RequireGameClosed();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam");
            var previous = key.GetValue("AutoLoginUser");
            key.SetValue("AutoLoginUser", account.AccountName, RegistryValueKind.String);
            try { OpenSteam(); }
            catch
            {
                if (previous is null) key.DeleteValue("AutoLoginUser", false);
                else key.SetValue("AutoLoginUser", previous);
                throw;
            }
        }
        finally { AccountSwitch.Release(); }
    }
    public static async Task CloseSteam()
    {
        if (!SteamIsRunning()) return;
        using var shutdown = Process.Start(new ProcessStartInfo(SteamExe, "-shutdown") { UseShellExecute = true });
        for (var i = 0; i < 30; i++)
        {
            if (!SteamIsRunning()) return;
            await Task.Delay(500);
        }
        throw new IOException("Steam is still running. Close it and try again.");
    }
    private static bool SteamIsRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void OpenSteam()
    {
        if (!File.Exists(SteamExe)) throw new IOException("Steam was not found. Install or open Steam first.");
        using var process = Process.Start(new ProcessStartInfo(SteamExe, "-silent") { UseShellExecute = true });
    }
}
