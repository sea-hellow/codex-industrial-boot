using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace CodexIndustrial
{
    public sealed class Settings
    {
        public string AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        public string Executable = "", Arguments = "", Video = "media/startup.mp4";
        public bool RespectReducedMotion = true;
        public static Settings Load(string root)
        {
            var s = new Settings();
            using (var reader = XmlReader.Create(Path.Combine(root, "launcher.config.xml"),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var xml = XDocument.Load(reader).Root;
                if (xml == null) throw new InvalidDataException("启动配置为空。");
                Func<string, string, string> get = (key, fallback) => xml.Element(key) == null ? fallback : xml.Element(key).Value.Trim();
                s.AppId = get("appUserModelId", s.AppId);
                s.Executable = get("executablePath", "");
                s.Arguments = get("executableArguments", "");
                s.Video = get("videoFile", s.Video);
                s.RespectReducedMotion = bool.Parse(get("respectReducedMotion", "true"));
                if (s.AppId.Length == 0 && s.Executable.Length == 0) throw new InvalidDataException("未配置 Codex 桌面程序。");
            }
            return s;
        }
    }

    public static class Activation
    {
        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class Manager { }
        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
                [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint pid);
            [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr items,
                [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint pid);
            [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr items, out uint pid);
        }
        public static uint Open(Settings settings, string root)
        {
            if (settings.Executable.Length > 0)
            {
                string path = Environment.ExpandEnvironmentVariables(settings.Executable);
                if (!Path.IsPathRooted(path)) path = Path.Combine(root, path);
                path = Path.GetFullPath(path);
                if (string.Equals(path, Process.GetCurrentProcess().MainModule.FileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Codex 目标不能是动画启动器自身。");
                if (!File.Exists(path)) throw new FileNotFoundException("找不到 Codex 桌面程序。", path);
                var p = Process.Start(new ProcessStartInfo(path, settings.Arguments) { UseShellExecute = true });
                return p == null ? 0 : (uint)p.Id;
            }
            var manager = (IManager)new Manager();
            try
            {
                uint pid;
                Marshal.ThrowExceptionForHR(manager.ActivateApplication(settings.AppId, "", 0, out pid));
                return pid;
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
    }

    public sealed class Session
    {
        private readonly string file;
        public readonly Dictionary<string, object> Data = new Dictionary<string, object>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        public Session(string path, bool preview)
        {
            file = path;
            Data["version"] = "1.0.1"; Data["preview"] = preview;
            Data["started_utc"] = DateTime.UtcNow.ToString("o");
            foreach (string key in new[] { "media_opened", "completed", "activation_attempted", "activation_succeeded", "app_window_visible" }) Data[key] = false;
        }
        public void Save()
        {
            Data["elapsed_seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 3);
            Data["finished_utc"] = DateTime.UtcNow.ToString("o");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(Data));
                // Preserve the previous valid report if writing the new one fails.
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            catch { /* Diagnostic writes must never prevent opening the app. */ }
        }
    }

    public sealed class Splash : Window
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        private delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
        private readonly Settings settings;
        private readonly Session session;
        private readonly string root;
        private readonly bool preview, reduced;
        private readonly double skipAfter;
        private readonly MediaElement player;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Stopwatch handoff = new Stopwatch();
        private bool finishing, cleaned, opened;
        private double duration = 32, position, progressAt;
        private uint appPid;
        private IntPtr appWindow;
        private readonly TextBlock status;
        public Splash(Settings s, Session log, string folder, bool onlyPreview, bool reduce, double skip)
        {
            settings = s; session = log; root = folder; preview = onlyPreview; reduced = reduce; skipAfter = skip;
            Title = "Codex Industrial · 非官方启动动画";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false; Background = Brushes.Black;
            Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += delegate
            {
                var bounds = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
            };
            var grid = new Grid { Background = Brushes.Black, UseLayoutRounding = true };
            player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.Uniform, IsMuted = true, Volume = 0, IsHitTestVisible = false, SnapsToDevicePixels = true };
            grid.Children.Add(player);
            status = new TextBlock { Text = "", Foreground = Brushes.White, FontSize = 14,
                Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
            grid.Children.Add(status);
            var skipButton = new Button { Content = "跳过  ›", ToolTip = "跳过动画并打开 Codex（Esc / Enter / 空格）",
                Foreground = new SolidColorBrush(Color.FromRgb(210, 230, 240)), Background = new SolidColorBrush(Color.FromArgb(160, 8, 16, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(65, 90, 105)), Padding = new Thickness(14, 7, 14, 7),
                FontSize = 13, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Focusable = false };
            skipButton.Click += delegate { Finish("skipped"); };
            grid.Children.Add(skipButton); Content = grid;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; Finish("skipped"); }
            };
            player.MediaOpened += delegate
            {
                if (finishing) return;
                opened = true; session.Data["media_opened"] = true;
                session.Data["video_width"] = player.NaturalVideoWidth; session.Data["video_height"] = player.NaturalVideoHeight;
                if (player.NaturalDuration.HasTimeSpan) duration = player.NaturalDuration.TimeSpan.TotalSeconds;
                session.Data["duration_seconds"] = duration;
                if (duration <= 0 || duration > 180 || double.IsNaN(duration)) { Fail("视频时长无效。"); return; }
                progressAt = clock.Elapsed.TotalSeconds;
            };
            player.MediaEnded += delegate { session.Data["completed"] = true; Finish("completed"); };
            player.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e) { Fail(e.ErrorException == null ? "视频无法解码。" : e.ErrorException.Message); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick;
            ContentRendered += delegate
            {
                Activate(); Focus(); clock.Start(); timer.Start();
                try
                {
                    if (reduced) { status.Text = "Codex · 正在启动"; return; }
                    string video = Path.GetFullPath(Path.Combine(root, settings.Video));
                    if (!File.Exists(video)) { Fail("启动视频不存在。"); return; }
                    player.Source = new Uri(video); player.Play();
                }
                catch (Exception e) { Fail(e.Message); }
            };
            Closing += delegate { if (!finishing) Finish("window_closed"); };
            Closed += delegate { cleaned = true; timer.Stop(); try { player.Close(); } catch { } session.Save(); };
        }
        private void Tick(object sender, EventArgs e)
        {
            if (cleaned) return;
            if (finishing)
            {
                bool visible = FindApplicationWindow();
                if (visible || handoff.Elapsed.TotalSeconds >= 5)
                {
                    session.Data["app_window_visible"] = visible;
                    if (visible) { if (IsIconic(appWindow)) ShowWindow(appWindow, 9); SetForegroundWindow(appWindow); }
                    Close();
                }
                return;
            }
            double elapsed = clock.Elapsed.TotalSeconds;
            if (skipAfter >= 0 && elapsed >= skipAfter) { Finish("diagnostic_skip"); return; }
            if (reduced) { if (elapsed >= 0.6) Finish("reduced_motion"); return; }
            if (!opened) { if (elapsed > 10) Fail("视频打开超时。"); return; }
            double current = player.Position.TotalSeconds;
            if (current > position + 0.005) { position = current; progressAt = elapsed; }
            session.Data["max_position_seconds"] = Math.Round(position, 3);
            if (elapsed - progressAt > 6) Fail("视频播放停止超过 6 秒。");
            else if (elapsed > duration + 20) Fail("视频播放超时。");
        }
        private bool FindApplicationWindow()
        {
            bool found = false;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (!IsWindowVisible(window)) return true;
                uint pid; GetWindowThreadProcessId(window, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return true;
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                    {
                        bool match = pid == appPid;
                        if (!match && settings.Executable.Length == 0)
                        {
                            uint size = 1024; var id = new StringBuilder((int)size);
                            match = GetApplicationUserModelId(process.Handle, ref size, id) == 0 && string.Equals(id.ToString(), settings.AppId, StringComparison.OrdinalIgnoreCase);
                        }
                        if (!match && settings.Executable.Length > 0)
                        {
                            string target = Environment.ExpandEnvironmentVariables(settings.Executable);
                            if (!Path.IsPathRooted(target)) target = Path.Combine(root, target);
                            match = string.Equals(process.MainModule.FileName, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
                        }
                        if (!match) return true;
                    }
                    found = true; appWindow = window; session.Data["visible_app_pid"] = pid;
                    return false;
                }
                catch { return true; }
            }, IntPtr.Zero);
            return found;
        }
        private void Fail(string error)
        {
            if (finishing) return;
            session.Data["media_error"] = error;
            Finish("media_failed");
        }
        private void Finish(string reason)
        {
            if (finishing) return;
            finishing = true; session.Data["reason"] = reason;
            try { player.Pause(); } catch { }
            if (preview) { Dispatcher.BeginInvoke(new Action(Close)); return; }
            session.Data["activation_attempted"] = true;
            try
            {
                appPid = Activation.Open(settings, root);
                session.Data["activation_succeeded"] = true; session.Data["app_pid"] = appPid;
                status.Text = "正在进入 Codex…";
                handoff.Start();
                // Closing an ordinary splash must still open Codex; no second activation is needed.
                if (reason == "window_closed") session.Save();
            }
            catch (Exception e)
            {
                session.Data["activation_error"] = e.Message; session.Save();
                Topmost = false;
                MessageBox.Show("无法打开 Codex，请先确认桌面版已安装。\n\n" + e.Message, "Codex Industrial", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }

    public static class Program
    {
        private static string Value(string[] args, string name)
        { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        [STAThread]
        public static int Main(string[] args)
        {
            bool preview = Array.IndexOf(args, "--preview") >= 0;
            bool check = Array.IndexOf(args, "--check") >= 0;
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string logRoot = File.Exists(Path.Combine(root, ".codex-industrial-owner")) ? root : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexIndustrialNative");
            string report = Value(args, "--report") ?? Path.Combine(logRoot, "logs", preview ? "last-preview.json" : "last-session.json");
            var session = new Session(Path.GetFullPath(report), preview);
            var settings = new Settings();
            try
            {
                try { settings = Settings.Load(root); }
                catch (Exception e) { session.Data["config_error"] = e.Message; if (check) throw; }
                if (check)
                {
                    if (!File.Exists(Path.Combine(root, settings.Video))) throw new FileNotFoundException("视频不存在。");
                    session.Data["reason"] = "check_passed"; session.Save(); return 0;
                }
                bool owned;
                using (var mutex = new Mutex(true, "Local\\CodexIndustrialNative-" + (preview ? "Preview" : "Launch"), out owned))
                {
                    if (!owned) { session.Data["reason"] = "already_running"; session.Save(); return 0; }
                    try
                    {
                        bool reduce = Array.IndexOf(args, "--reduced-motion") >= 0 || (settings.RespectReducedMotion && !SystemParameters.ClientAreaAnimation);
                        if (Array.IndexOf(args, "--force-animation") >= 0) reduce = false;
                        double skip = -1;
                        string value = Value(args, "--skip-after");
                        if (value != null && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out skip) || skip < 0))
                            throw new ArgumentException("skip-after 必须是非负秒数。");
                        new Application().Run(new Splash(settings, session, root, preview, reduce, skip));
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return session.Data.ContainsKey("activation_error") || (preview && session.Data.ContainsKey("media_error")) ? 1 : 0;
            }
            catch (Exception e)
            {
                session.Data["fatal_error"] = e.ToString();
                if (!preview && !check && !(bool)session.Data["activation_attempted"])
                {
                    session.Data["activation_attempted"] = true;
                    try { session.Data["app_pid"] = Activation.Open(settings, root); session.Data["activation_succeeded"] = true; }
                    catch (Exception activationError) { session.Data["activation_error"] = activationError.Message; }
                }
                session.Save(); return 1;
            }
        }
    }
}
