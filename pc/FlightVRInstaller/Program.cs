using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using Microsoft.Win32;

namespace FlightVRInstaller;

internal static class Program
{
    const string AlvrUrl = "https://github.com/alvr-org/ALVR/releases/download/v20.8.0/alvr_streamer_windows.zip";
    const string PhoneVrUrl = "https://github.com/PhoneVR-Developers/PhoneVR/releases/download/v2.0.0-beta/PhoneVR-v2.0.0-beta.apk";

    static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlightVR");
    static readonly string AlvrDir = Path.Combine(BaseDir, "ALVR");
    static readonly string DownloadsDir = Path.Combine(BaseDir, "Downloads");
    static readonly string AlvrZip = Path.Combine(DownloadsDir, "alvr_streamer_windows_v20.8.0.zip");
    static readonly string PhoneVrApk = Path.Combine(DownloadsDir, "PhoneVR-v2.0.0-beta.apk");

    static async Task<int> Main()
    {
        Console.Title = "FlightVR Installer 2.1";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Este instalador é somente para Windows 10/11.");
            return 1;
        }

        if (!IsAdministrator())
        {
            try
            {
                var exe = Environment.ProcessPath!;
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
                return 0;
            }
            catch
            {
                Console.WriteLine("O FlightVR precisa de permissão de administrador para configurar firewall e OpenXR.");
                Console.ReadKey();
                return 2;
            }
        }

        Header();
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(DownloadsDir);

        try
        {
            await Download("ALVR 20.8.0", AlvrUrl, AlvrZip);
            await Download("PhoneVR 2.0.0 beta", PhoneVrUrl, PhoneVrApk);

            Console.WriteLine("\n[3/7] Instalando ALVR...");
            if (Directory.Exists(AlvrDir))
            {
                try { Directory.Delete(AlvrDir, true); } catch { }
            }
            Directory.CreateDirectory(AlvrDir);
            ZipFile.ExtractToDirectory(AlvrZip, AlvrDir, true);

            var dashboard = FindDashboard();
            if (dashboard is null)
                throw new Exception("Não encontrei o executável do ALVR após a extração.");

            Console.WriteLine("[4/7] Configurando Firewall...");
            ConfigureFirewall(dashboard);

            Console.WriteLine("[5/7] Configurando SteamVR/OpenXR...");
            var openxr = FindSteamVrOpenXr();
            bool openxrConfigured = false;
            if (openxr is not null)
            {
                openxrConfigured = SetActiveOpenXrRuntime(openxr);
                Console.WriteLine(openxrConfigured
                    ? $"      OpenXR ativo: {openxr}"
                    : "      Não foi possível definir o runtime OpenXR automaticamente.");
            }
            else
            {
                Console.WriteLine("      SteamVR ainda não foi encontrado. O atalho FlightVR abrirá a instalação pelo Steam.");
            }

            Console.WriteLine("[6/7] Criando atalhos e configuração...");
            var startBat = WriteLauncher(dashboard);
            WriteReadme(dashboard, openxr, openxrConfigured);
            CreateShortcut(startBat);
            TryCopyPhoneVrToDesktop();

            Console.WriteLine("[7/7] Finalizando...");
            var ip = GetLocalIpv4();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✓ FLIGHTVR INSTALADO");
            Console.ResetColor();
            Console.WriteLine($"\nPasta: {BaseDir}");
            Console.WriteLine($"IP deste PC: {ip ?? "não detectado"}");
            Console.WriteLine($"APK PhoneVR: {PhoneVrApk}");
            Console.WriteLine("\nNO CELULAR:");
            Console.WriteLine("  1. Instale PhoneVR-v2.0.0-beta.apk.");
            Console.WriteLine("  2. Celular e PC devem estar no mesmo Wi-Fi 5/6 GHz.");
            Console.WriteLine("  3. Coloque o celular no visor tipo Cardboard.");
            Console.WriteLine("\nNO PC:");
            Console.WriteLine("  1. Abra o atalho FlightVR na Área de Trabalho.");
            Console.WriteLine("  2. No ALVR, aceite/conecte o cliente quando aparecer.");
            Console.WriteLine("  3. Abra o MSFS 2020 e pressione Ctrl+Tab para entrar em VR.");
            Console.WriteLine("\nO estéreo é real: SteamVR renderiza uma câmera por olho e o ALVR envia as duas vistas ao telefone.");

            try { Process.Start(new ProcessStartInfo(BaseDir) { UseShellExecute = true }); } catch { }
            try { Process.Start(new ProcessStartInfo(dashboard) { UseShellExecute = true }); } catch { }

            Console.WriteLine("\nPressione qualquer tecla para fechar.");
            Console.ReadKey();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nERRO: " + ex.Message);
            Console.ResetColor();
            Console.WriteLine("\nNada foi alterado no Flight Simulator. Você pode executar o instalador novamente.");
            Console.ReadKey();
            return 10;
        }
    }

    static void Header()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine("            FLIGHTVR INSTALLER 2.1");
        Console.WriteLine("       MSFS 2020 + Smartphone Stereo VR");
        Console.WriteLine("==================================================");
        Console.ResetColor();
        Console.WriteLine("Base: ALVR 20.8.0 + PhoneVR 2.0.0 beta\n");
    }

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static async Task Download(string name, string url, string output)
    {
        if (File.Exists(output) && new FileInfo(output).Length > 1_000_000)
        {
            Console.WriteLine($"[OK] {name} já baixado.");
            return;
        }

        Console.WriteLine($"Baixando {name}...");
        using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightVR-Installer/2.1");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var outputStream = File.Create(output);
        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        int read;
        int lastPct = -1;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await outputStream.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total > 0)
            {
                var pct = (int)(readTotal * 100 / total);
                if (pct / 5 != lastPct / 5)
                {
                    Console.Write($"\r      {pct,3}%   ");
                    lastPct = pct;
                }
            }
        }
        Console.WriteLine("\r      100%   ");
    }

    static string? FindDashboard()
    {
        var preferred = Directory.EnumerateFiles(AlvrDir, "alvr_dashboard.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (preferred is not null) return preferred;

        preferred = Directory.EnumerateFiles(AlvrDir, "alvr_launcher.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (preferred is not null) return preferred;

        return Directory.EnumerateFiles(AlvrDir, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(x => Path.GetFileName(x).Contains("alvr", StringComparison.OrdinalIgnoreCase));
    }

    static void ConfigureFirewall(string dashboard)
    {
        RunHidden("netsh", $"advfirewall firewall delete rule name=\"FlightVR ALVR Inbound\"");
        RunHidden("netsh", $"advfirewall firewall delete rule name=\"FlightVR ALVR Outbound\"");
        RunHidden("netsh", $"advfirewall firewall add rule name=\"FlightVR ALVR Inbound\" dir=in action=allow program=\"{dashboard}\" enable=yes profile=private");
        RunHidden("netsh", $"advfirewall firewall add rule name=\"FlightVR ALVR Outbound\" dir=out action=allow program=\"{dashboard}\" enable=yes profile=private");
    }

    static string? FindSteamVrOpenXr()
    {
        var candidates = new List<string>();

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        candidates.Add(Path.Combine(pf86, "Steam", "steamapps", "common", "SteamVR", "steamxr_win64.json"));

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = key?.GetValue("SteamPath")?.ToString()?.Replace('/', Path.DirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(steamPath))
            candidates.Add(Path.Combine(steamPath, "steamapps", "common", "SteamVR", "steamxr_win64.json"));

        return candidates.FirstOrDefault(File.Exists);
    }

    static bool SetActiveOpenXrRuntime(string json)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Khronos\OpenXR\1", true);
            key.SetValue("ActiveRuntime", json, RegistryValueKind.String);
            return true;
        }
        catch { return false; }
    }

    static string WriteLauncher(string dashboard)
    {
        var bat = Path.Combine(BaseDir, "FlightVR-Start.cmd");
        var steamVr = "steam://rungameid/250820";
        var lines = new[]
        {
            "@echo off",
            "title FlightVR",
            "echo Iniciando SteamVR e ALVR...",
            $"start \"\" \"{steamVr}\"",
            "timeout /t 4 /nobreak >nul",
            $"start \"\" \"{dashboard}\"",
            "exit"
        };
        File.WriteAllLines(bat, lines);
        return bat;
    }

    static void WriteReadme(string dashboard, string? openxr, bool configured)
    {
        var readme = Path.Combine(BaseDir, "LEIA-ME-FlightVR.txt");
        File.WriteAllText(readme, $"""
FLIGHTVR 2.1 - MSFS 2020 + Smartphone VR

INSTALADO EM:
{BaseDir}

ALVR:
{dashboard}

PHONEVR APK:
{PhoneVrApk}

OPENXR:
{(openxr ?? "SteamVR não encontrado durante a instalação")}
Configurado automaticamente: {(configured ? "SIM" : "NÃO")}

COMO USAR
1) Instale o APK PhoneVR no celular.
2) PC e celular na mesma rede Wi-Fi; 5/6 GHz é recomendado.
3) Abra o atalho FlightVR.
4) No ALVR, confie/aceite o celular quando ele aparecer.
5) Coloque o telefone no óculos/Cardboard.
6) Abra Microsoft Flight Simulator 2020.
7) Dentro do cockpit pressione Ctrl+Tab para alternar para VR.

ESTÉREO
A profundidade não é uma duplicação de tela. SteamVR/OpenXR entrega duas vistas,
uma para cada olho, e o ALVR codifica e envia o par estereoscópico para o PhoneVR.

OBSERVAÇÃO
FlightVR combina componentes de código aberto de terceiros. ALVR e PhoneVR mantêm
suas próprias licenças, projetos e atualizações.
""");
    }

    static void CreateShortcut(string startBat)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "FlightVR.lnk");
            var escapedLink = link.Replace("'", "''");
            var escapedTarget = startBat.Replace("'", "''");
            var ps = $"$w=New-Object -ComObject WScript.Shell;$s=$w.CreateShortcut('{escapedLink}');$s.TargetPath='{escapedTarget}';$s.WorkingDirectory='{BaseDir.Replace("'", "''")}';$s.Description='FlightVR - MSFS Smartphone VR';$s.Save()";
            RunHidden("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
        }
        catch { }
    }

    static void TryCopyPhoneVrToDesktop()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            File.Copy(PhoneVrApk, Path.Combine(desktop, Path.GetFileName(PhoneVrApk)), true);
        }
        catch { }
    }

    static void RunHidden(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(15000);
        }
        catch { }
    }

    static string? GetLocalIpv4()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString();
        }
        catch { return null; }
    }
}
