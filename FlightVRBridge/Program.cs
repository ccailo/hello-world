using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace FlightVRBridge;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private const string AlvrVersion = "20.8.0";
    private const string AlvrUrl = "https://github.com/alvr-org/ALVR/releases/download/v20.8.0/alvr_streamer_windows.zip";
    private const string PhoneVrUrl = "https://github.com/PhoneVR-Developers/PhoneVR/releases/download/v2.0.0-beta/PhoneVR-v2.0.0-beta.apk";

    private readonly string baseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlightVRBridge");
    private string AlvrDir => Path.Combine(baseDir, "ALVR");
    private string DownloadsDir => Path.Combine(baseDir, "Downloads");
    private string AlvrZip => Path.Combine(DownloadsDir, "alvr_streamer_windows_20.8.0.zip");
    private string PhoneVrApk => Path.Combine(DownloadsDir, "PhoneVR-v2.0.0-beta.apk");

    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = true });
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 9, Style = ProgressBarStyle.Continuous };
    private readonly Label status = new() { AutoSize = false, Height = 28, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox diagnostic = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(15, 20, 28),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9.5f)
    };

    private readonly Button installButton = MakeButton("1  INSTALAR MOTOR VR", true);
    private readonly Button openAlvrButton = MakeButton("2  ABRIR ALVR");
    private readonly Button openSteamVrButton = MakeButton("3  ABRIR STEAMVR");
    private readonly Button openXrButton = MakeButton("4  DEFINIR STEAMVR COMO OPENXR");
    private readonly Button adbButton = MakeButton("5  INSTALAR PHONEVR VIA USB/ADB");
    private readonly Button phoneFolderButton = MakeButton("ABRIR PASTA DO APK");
    private readonly Button flightModeButton = MakeButton("✈  INICIAR MODO FLIGHT SIMULATOR", true);
    private readonly Button refreshButton = MakeButton("ATUALIZAR DIAGNÓSTICO");

    private static Button MakeButton(string text, bool accent = false)
    {
        return new Button
        {
            Text = text,
            Height = 44,
            Dock = DockStyle.Top,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent ? Color.FromArgb(31, 111, 235) : Color.FromArgb(31, 39, 52),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    public MainForm()
    {
        Text = "FlightVR Bridge 1.0 — MSFS 2020";
        Width = 900;
        Height = 720;
        MinimumSize = new Size(780, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(7, 11, 18);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        http.DefaultRequestHeaders.UserAgent.ParseAdd("FlightVR-Bridge/1.0");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 16, 0), AutoScroll = true };
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };

        var title = new Label
        {
            Text = "FLIGHTVR BRIDGE",
            Dock = DockStyle.Top,
            Height = 42,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 21, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "Smartphone como HMD estereoscópico\nMSFS 2020 • SteamVR • ALVR • PhoneVR",
            Dock = DockStyle.Top,
            Height = 58,
            ForeColor = Color.FromArgb(160, 174, 195)
        };
        var engine = new Label
        {
            Text = "Motor fixado: ALVR 20.8.0 + PhoneVR 2.0.0-beta",
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = Color.FromArgb(100, 230, 190)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 430,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false
        };
        foreach (var b in new[] { installButton, openAlvrButton, openSteamVrButton, openXrButton, adbButton, phoneFolderButton, flightModeButton, refreshButton })
        {
            b.Width = 338;
            buttons.Controls.Add(b);
        }

        var note = new Label
        {
            Text = "Fluxo VR real:\nMSFS → OpenXR/SteamVR → ALVR → PhoneVR → dois olhos.\n\nWi‑Fi 5 GHz é fortemente recomendado. PC por Ethernet reduz latência.",
            Dock = DockStyle.Top,
            Height = 110,
            ForeColor = Color.FromArgb(180, 190, 205)
        };

        left.Controls.Add(note);
        left.Controls.Add(buttons);
        left.Controls.Add(engine);
        left.Controls.Add(subtitle);
        left.Controls.Add(title);

        var diagTitle = new Label
        {
            Text = "DIAGNÓSTICO",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 13, FontStyle.Bold)
        };
        right.Controls.Add(diagnostic);
        right.Controls.Add(status);
        right.Controls.Add(progress);
        right.Controls.Add(diagTitle);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);

        installButton.Click += async (_, _) => await InstallEngineAsync();
        openAlvrButton.Click += (_, _) => OpenAlvr();
        openSteamVrButton.Click += (_, _) => OpenSteamVr();
        openXrButton.Click += (_, _) => SetSteamVrOpenXr();
        adbButton.Click += async (_, _) => await InstallPhoneVrViaAdbAsync();
        phoneFolderButton.Click += (_, _) => OpenApkFolder();
        flightModeButton.Click += async (_, _) => await StartFlightModeAsync();
        refreshButton.Click += (_, _) => RefreshDiagnostic();

        Shown += (_, _) => RefreshDiagnostic();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) http.Dispose();
        base.Dispose(disposing);
    }

    private async Task InstallEngineAsync()
    {
        try
        {
            SetBusy(true);
            Directory.CreateDirectory(DownloadsDir);
            Directory.CreateDirectory(baseDir);

            Status("Baixando ALVR 20.8.0…");
            await DownloadAsync(AlvrUrl, AlvrZip);

            Status("Baixando PhoneVR para Android…");
            await DownloadAsync(PhoneVrUrl, PhoneVrApk);

            Status("Extraindo ALVR…");
            progress.Value = 0;
            if (Directory.Exists(AlvrDir))
            {
                try { Directory.Delete(AlvrDir, true); }
                catch
                {
                    MessageBox.Show("Feche o ALVR antes de reinstalar/atualizar.", "FlightVR Bridge",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            Directory.CreateDirectory(AlvrDir);
            ZipFile.ExtractToDirectory(AlvrZip, AlvrDir, true);
            progress.Value = 100;

            var dash = FindAlvrDashboard();
            if (dash == null)
                throw new FileNotFoundException("ALVR Dashboard.exe não foi encontrado após a extração.");

            File.WriteAllText(Path.Combine(baseDir, "installed.txt"),
                $"FlightVR Bridge 1.0{Environment.NewLine}ALVR={AlvrVersion}{Environment.NewLine}Installed={DateTimeOffset.Now:O}{Environment.NewLine}");

            Status("Motor VR instalado. Abrindo ALVR…");
            RefreshDiagnostic();
            Process.Start(new ProcessStartInfo(dash) { WorkingDirectory = Path.GetDirectoryName(dash)!, UseShellExecute = true });

            MessageBox.Show(
                "Motor VR instalado.\n\nNo ALVR, conclua o assistente inicial e permita as regras de firewall quando solicitado.\n\nDepois instale o PhoneVR no celular e escolha ALVR dentro dele.",
                "FlightVR Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Status("ERRO: " + ex.Message);
            MessageBox.Show(ex.ToString(), "Falha na instalação", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DownloadAsync(string url, string destination)
    {
        var temp = destination + ".part";
        if (File.Exists(temp)) File.Delete(temp);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1L;

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total > 0)
            {
                int pct = (int)Math.Clamp(readTotal * 100 / total, 0, 100);
                progress.Value = pct;
                status.Text = $"{status.Tag}  {pct}%  ({readTotal / 1024 / 1024} MB / {total / 1024 / 1024} MB)";
                Application.DoEvents();
            }
        }
        await output.FlushAsync();
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
        progress.Value = 100;
    }

    private void OpenAlvr()
    {
        var dash = FindAlvrDashboard();
        if (dash == null)
        {
            MessageBox.Show("ALVR ainda não está instalado. Clique em INSTALAR MOTOR VR.", "FlightVR Bridge");
            return;
        }
        Process.Start(new ProcessStartInfo(dash) { WorkingDirectory = Path.GetDirectoryName(dash)!, UseShellExecute = true });
        Status("ALVR aberto.");
    }

    private void OpenSteamVr()
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://rungameid/250820") { UseShellExecute = true });
            Status("Solicitação para abrir SteamVR enviada.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não consegui abrir SteamVR via Steam.\n" + ex.Message, "FlightVR Bridge");
        }
    }

    private void SetSteamVrOpenXr()
    {
        var runtime = FindSteamVrRuntime();
        if (runtime == null)
        {
            MessageBox.Show("Não encontrei steamxr_win64.json. Instale o SteamVR e abra-o pelo menos uma vez.", "FlightVR Bridge",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var args = $"add \"HKLM\\SOFTWARE\\Khronos\\OpenXR\\1\" /v ActiveRuntime /t REG_SZ /d \"{runtime}\" /f";
            var p = Process.Start(new ProcessStartInfo("reg.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            p?.WaitForExit();
            RefreshDiagnostic();
            Status("SteamVR configurado como runtime OpenXR.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Status("Alteração do OpenXR cancelada pelo usuário.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao configurar OpenXR", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task InstallPhoneVrViaAdbAsync()
    {
        if (!File.Exists(PhoneVrApk))
        {
            MessageBox.Show("O APK ainda não foi baixado. Clique primeiro em INSTALAR MOTOR VR.", "FlightVR Bridge");
            return;
        }

        var adb = FindAdb();
        if (adb == null)
        {
            OpenApkFolder();
            MessageBox.Show(
                "ADB não foi encontrado neste PC.\n\nAbri a pasta do APK. Transfira PhoneVR-v2.0.0-beta.apk ao celular e instale manualmente.\n\nSe quiser instalação automática, instale Android Platform Tools e habilite Depuração USB.",
                "ADB não encontrado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true);
            Status("Verificando celular via ADB…");
            var devices = await RunCaptureAsync(adb, "devices");
            if (!devices.Split('\n').Any(l => l.Trim().EndsWith("\tdevice", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    "Nenhum celular autorizado foi encontrado.\n\nAtive Opções do desenvolvedor → Depuração USB e aceite a autorização RSA no celular.",
                    "PhoneVR via ADB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Status("Instalando PhoneVR no celular…");
            var result = await RunCaptureAsync(adb, $"install -r \"{PhoneVrApk}\"");
            if (result.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                Status("PhoneVR instalado no celular.");
                MessageBox.Show("PhoneVR instalado com sucesso.", "FlightVR Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(result, "ADB retornou um erro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshDiagnostic();
        }
    }

    private async Task StartFlightModeAsync()
    {
        var dash = FindAlvrDashboard();
        if (dash == null)
        {
            var answer = MessageBox.Show("ALVR ainda não está instalado. Instalar agora?", "FlightVR Bridge",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
                await InstallEngineAsync();
            dash = FindAlvrDashboard();
            if (dash == null) return;
        }

        OpenAlvr();
        await Task.Delay(700);
        OpenSteamVr();

        MessageBox.Show(
            "MODO FLIGHT SIMULATOR\n\n" +
            "1. No celular, abra PhoneVR e escolha ALVR.\n" +
            "2. No ALVR Dashboard, aceite/TRUST o telefone quando ele aparecer.\n" +
            "3. Confirme que SteamVR é o runtime OpenXR (botão 4).\n" +
            "4. Abra o Microsoft Flight Simulator 2020.\n" +
            "5. Dentro do voo, use Ctrl+Tab para entrar no modo VR.\n\n" +
            "A imagem que chega ao celular é gerada em duas perspectivas separadas pelo compositor VR.",
            "FlightVR — pronto para voo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenApkFolder()
    {
        Directory.CreateDirectory(DownloadsDir);
        if (File.Exists(PhoneVrApk))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{PhoneVrApk}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DownloadsDir}\"") { UseShellExecute = true });
    }

    private string? FindAlvrDashboard()
    {
        if (!Directory.Exists(AlvrDir)) return null;
        try
        {
            return Directory.EnumerateFiles(AlvrDir, "ALVR Dashboard.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(AlvrDir, "*alvr*dashboard*.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    private string? FindAdb()
    {
        var candidates = new List<string>();
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome)) candidates.Add(Path.Combine(androidHome, "platform-tools", "adb.exe"));
        if (!string.IsNullOrWhiteSpace(sdkRoot)) candidates.Add(Path.Combine(sdkRoot, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(p.Trim(), "adb.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath)) roots.Add(steamPath.Replace('/', '\\'));
        }
        catch { }

        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        foreach (var root in roots.ToArray())
        {
            try
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                var text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    var path = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path)) roots.Add(path);
                }
            }
            catch { }
        }

        return roots;
    }

    private string? FindSteamVrRuntime()
    {
        foreach (var root in SteamRoots())
        {
            var candidates = new[]
            {
                Path.Combine(root, "steamapps", "common", "SteamVR", "steamxr_win64.json"),
                Path.Combine(root, "steamapps", "common", "SteamVR", "bin", "win64", "steamxr_win64.json")
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }
        return null;
    }

    private string CurrentOpenXrRuntime()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
            return key?.GetValue("ActiveRuntime")?.ToString() ?? "(não definido)";
        }
        catch { return "(sem permissão para ler)"; }
    }

    private static async Task<string> RunCaptureAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar " + file);
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return stdout + Environment.NewLine + stderr;
    }

    private IEnumerable<string> LocalIpv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
            {
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
                    yield return $"{nic.Name}: {u.Address}";
            }
        }
    }

    private void RefreshDiagnostic()
    {
        var dash = FindAlvrDashboard();
        var runtime = FindSteamVrRuntime();
        var adb = FindAdb();
        var sb = new StringBuilder();

        sb.AppendLine("FlightVR Bridge 1.0");
        sb.AppendLine(new string('─', 54));
        sb.AppendLine($"Windows: {Environment.OSVersion}");
        sb.AppendLine($"ALVR 20.8.0: {(dash != null ? "INSTALADO" : "não instalado")}");
        if (dash != null) sb.AppendLine($"  {dash}");
        sb.AppendLine($"PhoneVR APK: {(File.Exists(PhoneVrApk) ? "BAIXADO" : "não baixado")}");
        sb.AppendLine($"ADB: {(adb ?? "não encontrado")}");
        sb.AppendLine();
        sb.AppendLine($"SteamVR runtime: {(runtime ?? "não encontrado")}");
        sb.AppendLine($"OpenXR ativo: {CurrentOpenXrRuntime()}");
        sb.AppendLine();

        var ips = LocalIpv4().ToList();
        sb.AppendLine("Rede / IPs locais:");
        if (ips.Count == 0) sb.AppendLine("  nenhum IPv4 ativo detectado");
        foreach (var ip in ips) sb.AppendLine("  " + ip);

        sb.AppendLine();
        sb.AppendLine("Checklist VR verdadeiro:");
        sb.AppendLine($"  [{(dash != null ? "x" : " ")}] ALVR instalado");
        sb.AppendLine($"  [{(File.Exists(PhoneVrApk) ? "x" : " ")}] PhoneVR APK disponível");
        sb.AppendLine($"  [{(runtime != null ? "x" : " ")}] SteamVR encontrado");
        sb.AppendLine("  [ ] PhoneVR aberto no celular em modo ALVR");
        sb.AppendLine("  [ ] Cliente do celular TRUSTED no ALVR Dashboard");
        sb.AppendLine("  [ ] MSFS alternado para VR com Ctrl+Tab");
        sb.AppendLine();
        sb.AppendLine("Sugestão inicial no ALVR:");
        sb.AppendLine("  60–72 Hz • HEVC/H.265 • 30–50 Mbps");
        sb.AppendLine("  reduza a resolução se a latência subir.");
        sb.AppendLine();
        sb.AppendLine("Arquitetura:");
        sb.AppendLine("  MSFS/OpenXR → SteamVR → ALVR encoder");
        sb.AppendLine("  → Wi‑Fi → PhoneVR → olho E + olho D");

        diagnostic.Text = sb.ToString();
        status.Text = dash != null ? "PRONTO: motor VR encontrado." : "Ainda não instalado.";
        progress.Value = 0;
    }

    private void Status(string message)
    {
        status.Tag = message;
        status.Text = message;
        status.Refresh();
    }

    private void SetBusy(bool busy)
    {
        foreach (var b in new[] { installButton, openAlvrButton, openSteamVrButton, openXrButton, adbButton, phoneFolderButton, flightModeButton, refreshButton })
            b.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }
}
