using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CppToExe;

public class MainForm : Form
{
    private readonly WebView2 _webView = new();
    private string? _currentSource;
    private string? _lastExe;
    private string? _lastRunBat;

    public MainForm()
    {
        Text = "C / C++  ->  EXE";
        Width = 860;
        Height = 640;
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;

        AllowDrop = true;
        DragEnter += Form_DragEnter;
        DragLeave += Form_DragLeave;
        DragDrop += Form_DragDrop;

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        Load += MainForm_Load;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var userData = Path.Combine(Path.GetTempPath(), "CppToExe_WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // let the host Form (not the browser) handle native OS file drag/drop
            _webView.AllowExternalDrop = false;

            _webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;

            _webView.CoreWebView2.NavigateToString(LoadEmbeddedHtml());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось запустить встроенный браузер WebView2.\r\n\r\n" +
                "Обычно это значит, что в системе нет WebView2 Runtime " +
                "(на Windows 11 он есть по умолчанию; на Windows 10 иногда нужно поставить отдельно " +
                "- 'Microsoft Edge WebView2 Runtime' через поиск в интернете).\r\n\r\n" +
                "Текст ошибки:\r\n" + ex.Message,
                "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------- drag & drop (handled by the host window) ----------------

    private void Form_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            PostToJs(new { type = "dragState", state = "over" });
        }
    }

    private void Form_DragLeave(object? sender, EventArgs e)
    {
        PostToJs(new { type = "dragState", state = "none" });
    }

    private void Form_DragDrop(object? sender, DragEventArgs e)
    {
        PostToJs(new { type = "dragState", state = "none" });
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            SelectSource(files[0]);
        }
    }

    // ---------------- messages from the HTML/JS side ----------------

    private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.WebMessageAsJson;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string type = root.GetProperty("type").GetString() ?? "";

        switch (type)
        {
            case "ready":
                SendStatus();
                break;

            case "browse":
                using (var dlg = new OpenFileDialog
                       {
                           Filter = "C/C++ (*.c;*.cpp;*.cc;*.cxx)|*.c;*.cpp;*.cc;*.cxx|Все файлы (*.*)|*.*"
                       })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) SelectSource(dlg.FileName);
                }
                break;

            case "build":
                var opts = root.GetProperty("opts");
                bool useStatic = GetBool(opts, "static");
                bool useOpt = GetBool(opts, "opt");
                bool useGui = GetBool(opts, "gui");
                bool pauseBat = GetBool(opts, "pauseBat");
                await BuildAsync(useStatic, useOpt, useGui, pauseBat);
                break;

            case "run":
                RunLastBuild();
                break;

            case "openFolder":
                if (_lastExe != null)
                    Process.Start("explorer.exe", $"/select,\"{_lastExe}\"");
                break;

            case "installCompiler":
                InstallCompiler();
                break;
        }
    }

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // ---------------- actions ----------------

    private void SelectSource(string path)
    {
        _currentSource = path;
        PostToJs(new { type = "fileSelected", path, name = Path.GetFileName(path) });
    }

    private void SendStatus()
    {
        string? gcc = FindCompiler("gcc");
        string? gpp = FindCompiler("g++");
        PostToJs(new { type = "status", gccFound = gcc != null, gppFound = gpp != null });
    }

    private void InstallCompiler()
    {
        PostToJs(new { type = "log", text = "Запускаю установку компилятора (WinLibs: gcc/g++) через winget." });
        PostToJs(new { type = "log", text = "Откроется отдельное окно. Когда оно закончит - закрой и снова открой это приложение." });
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install --id BrechtSanders.WinLibs.POSIX.UCRT -e " +
                            "--accept-source-agreements --accept-package-agreements",
                UseShellExecute = true
            });
        }
        catch
        {
            PostToJs(new { type = "log", text = "" });
            PostToJs(new { type = "log", text = "Не получилось запустить winget. Установи компилятор вручную:" });
            PostToJs(new { type = "log", text = "  1) Скачай MSYS2 с https://www.msys2.org/ и поставь" });
            PostToJs(new { type = "log", text = "  2) В окне 'MSYS2 UCRT64' выполни: pacman -S mingw-w64-ucrt-x86_64-gcc" });
            PostToJs(new { type = "log", text = "  3) Перезапусти это приложение" });
        }
    }

    private void RunLastBuild()
    {
        if (_lastRunBat != null && File.Exists(_lastRunBat))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _lastRunBat,
                WorkingDirectory = Path.GetDirectoryName(_lastRunBat),
                UseShellExecute = true
            });
        }
        else if (_lastExe != null && File.Exists(_lastExe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"\"{_lastExe}\"\"",
                WorkingDirectory = Path.GetDirectoryName(_lastExe),
                UseShellExecute = true
            });
        }
    }

    private async Task BuildAsync(bool useStatic, bool useOpt, bool useGui, bool pauseBat)
    {
        PostToJs(new { type = "buildStart" });
        _lastExe = null;
        _lastRunBat = null;

        if (string.IsNullOrWhiteSpace(_currentSource) || !File.Exists(_currentSource))
        {
            PostToJs(new { type = "log", text = "Файл не найден. Выбери .c или .cpp файл." });
            PostToJs(new { type = "buildResult", success = false });
            return;
        }

        string ext = Path.GetExtension(_currentSource).ToLowerInvariant();
        string cname = ext switch
        {
            ".c" => "gcc",
            ".cpp" or ".cc" or ".cxx" => "g++",
            _ => ""
        };
        if (cname == "")
        {
            PostToJs(new { type = "log", text = $"Неизвестное расширение '{ext}'. Нужен .c, .cpp, .cc или .cxx" });
            PostToJs(new { type = "buildResult", success = false });
            return;
        }

        string? compiler = FindCompiler(cname);
        if (compiler == null)
        {
            PostToJs(new { type = "log", text = $"Компилятор {cname} не найден на этом компьютере." });
            PostToJs(new { type = "log", text = "" });
            PostToJs(new { type = "log", text = "Нажми 'Установить компилятор' и дождись конца установки." });
            PostToJs(new { type = "buildResult", success = false });
            return;
        }

        string dir = Path.GetDirectoryName(_currentSource)!;
        string baseName = Path.GetFileNameWithoutExtension(_currentSource);
        string outExe = Path.Combine(dir, baseName + ".exe");
        string runBatPath = Path.Combine(dir, baseName + "_run.bat");

        if (File.Exists(outExe))
        {
            try { File.Delete(outExe); }
            catch
            {
                PostToJs(new { type = "log", text = $"Не могу перезаписать {outExe} - возможно, он запущен. Закрой его и повтори." });
                PostToJs(new { type = "buildResult", success = false });
                return;
            }
        }

        var args = new List<string> { Quote(_currentSource), "-o", Quote(outExe) };
        if (useOpt) args.Add("-O2");
        if (useStatic) args.Add("-static");
        if (useGui) args.Add("-mwindows");
        string argString = string.Join(" ", args);

        PostToJs(new { type = "log", text = $"Компилятор: {compiler}" });
        PostToJs(new { type = "log", text = $"Команда:    {cname} {argString}" });
        PostToJs(new { type = "log", text = "----------------------------------------" });
        PostToJs(new { type = "log", text = "Компиляция..." });

        var psi = new ProcessStartInfo
        {
            FileName = compiler,
            Arguments = argString,
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.EnvironmentVariables["PATH"] =
            Path.GetDirectoryName(compiler) + ";" + Environment.GetEnvironmentVariable("PATH");

        int exitCode;
        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, ev) => { if (ev.Data != null) PostToJs(new { type = "log", text = ev.Data }); };
            process.ErrorDataReceived += (_, ev) => { if (ev.Data != null) PostToJs(new { type = "log", text = ev.Data }); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            PostToJs(new { type = "log", text = $"Сбой запуска компилятора: {ex.Message}" });
            PostToJs(new { type = "buildResult", success = false });
            return;
        }

        PostToJs(new { type = "log", text = "----------------------------------------" });

        if (exitCode == 0 && File.Exists(outExe))
        {
            long kb = new FileInfo(outExe).Length / 1024;
            PostToJs(new { type = "log", text = $"ГОТОВО. EXE создан ({kb} КБ):" });
            PostToJs(new { type = "log", text = outExe });
            _lastExe = outExe;

            if (pauseBat)
            {
                string exeName = Path.GetFileName(outExe);
                string batContent =
                    "@echo off\r\n" +
                    $"title {baseName}\r\n" +
                    $"\"{exeName}\"\r\n" +
                    "echo.\r\n" +
                    "echo ----------------------------------------\r\n" +
                    "echo Программа завершена. Нажмите любую клавишу для выхода.\r\n" +
                    "pause >nul\r\n";
                File.WriteAllText(runBatPath, batContent, new UTF8Encoding(false));
                _lastRunBat = runBatPath;
                PostToJs(new { type = "log", text = "" });
                PostToJs(new { type = "log", text = "Создан запуск с паузой: " + runBatPath });
            }

            PostToJs(new { type = "buildResult", success = true, exePath = outExe, sizeKb = kb });
        }
        else
        {
            PostToJs(new { type = "log", text = $"ОШИБКА (код {exitCode}). EXE не создан. Прочитай сообщения выше." });
            PostToJs(new { type = "buildResult", success = false });
        }
    }

    private static string Quote(string s) => "\"" + s + "\"";

    // ---------------- helpers ----------------

    private void PostToJs(object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => PostToJsCore(json))); } catch { }
        }
        else
        {
            PostToJsCore(json);
        }
    }

    private void PostToJsCore(string json)
    {
        try { _webView.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
    }

    private static string LoadEmbeddedHtml()
    {
        var asm = typeof(MainForm).Assembly;
        string name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? FindCompiler(string name)
    {
        string exe = name + ".exe";

        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string p = Path.Combine(dir.Trim(), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }

        var candidates = new List<string>
        {
            @"C:\msys64\ucrt64\bin",
            @"C:\msys64\mingw64\bin",
            @"C:\mingw64\bin",
            @"C:\MinGW\bin",
            @"C:\Program Files\mingw-w64\bin",
        };

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string wg = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wg))
            {
                foreach (var d in Directory.GetDirectories(wg, "BrechtSanders.WinLibs*"))
                    candidates.Add(Path.Combine(d, "mingw64", "bin"));
            }
        }
        catch { }

        foreach (var dir in candidates)
        {
            string p = Path.Combine(dir, exe);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
