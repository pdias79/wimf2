using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Wimf.Tray;

internal static class Program
{
    private static Mutex? _singleton;

    [STAThread]
    private static void Main(string[] args)
    {
        // Prevent a second tray host from starting another server on the same port.
        _singleton = new Mutex(true, @"Local\Wimf2.Tray.Instance", out var firstInstance);
        if (!firstInstance)
        {
            _singleton.Dispose();
            MessageBox.Show("Wimf 2 est déjà lancé dans la zone de notification.", "Wimf 2",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplication(args));
        }
        finally
        {
            _singleton.ReleaseMutex();
            _singleton.Dispose();
        }
    }
}

internal sealed class TrayApplication : ApplicationContext
{
    private const string ServerUrl = "http://127.0.0.1:5080/";
    private readonly NotifyIcon _tray;
    private readonly Process _server = new();
    private readonly StringBuilder _output = new();
    private readonly object _gate = new();
    private Form? _logs;
    private bool _quitting;
    private bool _started;

    public TrayApplication(string[] args)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir la recherche", null, (_, _) => OpenSearch());
        menu.Items.Add("Messages du serveur", null, (_, _) => OpenLogs());
        menu.Items.Add("À propos", null, (_, _) => OpenAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter Wimf", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Icon = MakeMagnifierIcon(),
            Text = "Wimf 2 — Recherche de fichiers",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSearch(); };
        try { StartServer(args); }
        catch (Exception ex)
        {
            AddLog("Impossible de démarrer Wimf : " + ex.Message);
            _tray.ShowBalloonTip(5000, "Wimf 2", "Échec du démarrage : voir Messages du serveur.", ToolTipIcon.Error);
            OpenLogs();
        }
    }

    private void StartServer(string[] args)
    {
        // Prefer a colocated self-contained executable, then a colocated DLL.
        // --server allows development and custom deployment layouts.
        string? server = null;
        var forwarded = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--server")
            {
                if (server is not null || i + 1 >= args.Length)
                    throw new ArgumentException("--server nécessite un chemin unique.");
                server = args[++i];
            }
            else forwarded.Add(args[i]);
        }
        server ??= EmbeddedServer.ExtractIfPresent();
        server ??= new[] {
            Path.Combine(AppContext.BaseDirectory, "Wimf.Server.exe"),
            Path.Combine(AppContext.BaseDirectory, "Wimf.Server.dll"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Wimf.Server", "Wimf.Server.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Wimf.Server", "Wimf.Server.dll")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Wimf.Server", "bin", "Release", "net10.0", "Wimf.Server.dll"))
        }.FirstOrDefault(File.Exists);
        if (server is null || !File.Exists(server))
            throw new FileNotFoundException("Placez Wimf.Server.exe ou Wimf.Server.dll à côté de Wimf.Tray, ou utilisez --server CHEMIN.");
        var dll = Path.GetExtension(server).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(dll ? "dotnet" : Path.GetFullPath(server))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        if (dll) start.ArgumentList.Add(Path.GetFullPath(server));
        // Auto-detect a packaged config alongside the launcher when not specified.
        if (!forwarded.Contains("--config") &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "wimf.json")))
        {
            forwarded.Add("--config");
            forwarded.Add(Path.Combine(AppContext.BaseDirectory, "wimf.json"));
        }
        foreach (var arg in forwarded) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--tray-managed");
        _server.StartInfo = start;
        _server.OutputDataReceived += (_, e) => { if (e.Data is not null) AddLog(e.Data); };
        _server.ErrorDataReceived += (_, e) => { if (e.Data is not null) AddLog("[ERROR] " + e.Data); };
        _server.EnableRaisingEvents = true;
        _server.Exited += (_, _) =>
        {
            if (!_quitting) AddLog("Le serveur Wimf s'est arrêté.");
        };
        if (!_server.Start()) throw new InvalidOperationException("Démarrage du processus impossible.");
        _started = true;
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
        AddLog("Serveur démarré (PID " + _server.Id + "). URL : " + ServerUrl);
    }

    private void AddLog(string line)
    {
        lock (_gate)
        {
            _output.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line);
            if (_output.Length > 500_000) _output.Remove(0, _output.Length - 350_000);
        }
    }

    private void OpenSearch()
    {
        try { Process.Start(new ProcessStartInfo(ServerUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AddLog("Ouverture du navigateur impossible : " + ex.Message); OpenLogs(); }
    }

    private void OpenLogs()
    {
        if (_logs is { IsDisposed: false }) { _logs.Show(); _logs.Activate(); return; }
        var window = new Form { Text = "Wimf 2 — Messages du serveur", Width = 900, Height = 550, StartPosition = FormStartPosition.CenterScreen };
        var content = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        window.Controls.Add(content);
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        void Refresh()
        {
            string snapshot;
            lock (_gate) snapshot = _output.ToString();
            if (content.Text != snapshot) { content.Text = snapshot; content.SelectionStart = content.TextLength; content.ScrollToCaret(); }
        }
        timer.Tick += (_, _) => Refresh();
        window.FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); _logs = null; };
        _logs = window;
        Refresh();
        timer.Start();
        window.Show();
    }

    private void OpenAbout()
    {
        using var window = new Form
        {
            Text = "À propos de Wimf 2", Width = 400, Height = 325,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var icon = new PictureBox { Image = MakeMagnifierBitmap(80), SizeMode = PictureBoxSizeMode.Zoom,
            Width = 90, Height = 90, Left = 150, Top = 15 };
        var label = new Label { Text = "Wimf 2\nVersion " + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "2") +
            "\nPhilippe DIAS", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Left = 20, Top = 110, Width = 345, Height = 85 };
        var link = new LinkLabel { Text = "https://github.com/pdias79/wimf2",
            Left = 52, Top = 215, Width = 300, Height = 28, TextAlign = ContentAlignment.MiddleCenter };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(link.Text) { UseShellExecute = true });
        window.Controls.AddRange([icon, label, link]);
        window.ShowDialog();
        icon.Image?.Dispose();
    }

    private static Bitmap MakeMagnifierBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var bg = new SolidBrush(Color.FromArgb(21, 91, 188));
        g.FillRectangle(bg, 0, 0, size, size);
        using var glass = new Pen(Color.White, size * .07f);
        g.DrawEllipse(glass, size * .19f, size * .17f, size * .43f, size * .43f);
        using var handle = new Pen(Color.FromArgb(255, 194, 71), size * .14f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(handle, size * .59f, size * .60f, size * .81f, size * .82f);
        return bmp;
    }

    private static Icon MakeMagnifierIcon()
    {
        using var bitmap = MakeMagnifierBitmap(64);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    protected override void ExitThreadCore()
    {
        _quitting = true;
        _tray.Visible = false;
        _tray.Dispose();
        if (_started)
        {
            try
            {
                if (!_server.HasExited)
                {
                    _server.StandardInput.WriteLine("quit");
                    _server.StandardInput.Flush();
                    if (!_server.WaitForExit(8000))
                    {
                        AddLog("Délai d'arrêt du serveur dépassé : arrêt forcé.");
                        _server.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                AddLog("Arrêt du serveur : " + ex.Message);
            }
        }
        _server.Dispose();
        base.ExitThreadCore();
    }
}

internal static class EmbeddedServer
{
    private const string ResourceName = "Wimf.EmbeddedServer.exe";

    public static string? ExtractIfPresent()
    {
        using var resource = typeof(Program).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null) return null;

        // Version the cache by content so updates never overwrite a running server.
        // Stream the resource instead of allocating a second copy of a large EXE.
        var digest = Convert.ToHexString(SHA256.HashData(resource));
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wimf2", "server-runtime", digest);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "Wimf.Server.exe");
        if (File.Exists(target))
        {
            using var existing = File.OpenRead(target);
            if (Convert.ToHexString(SHA256.HashData(existing)) == digest)
                return target;
        }

        var staging = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = typeof(Program).Assembly.GetManifestResourceStream(ResourceName)!)
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            File.Move(staging, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
        return target;
    }
}
