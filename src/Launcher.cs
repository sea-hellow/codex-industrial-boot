using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace CodexIndustrial
{
    public sealed class Settings
    {
        public string AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        public string Executable = "", Arguments = "", Video = "media/startup.mp4";
        public string VideoFit = "cover";
        public double PrefetchSeconds = 4;
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
                string fit = get("videoFit", "cover").ToLowerInvariant();
                s.VideoFit = fit == "contain" ? "contain" : "cover";
                double prefetch;
                if (!double.TryParse(get("prefetchSeconds", "4"), NumberStyles.Float, CultureInfo.InvariantCulture, out prefetch)
                    || prefetch < 0 || prefetch > 30 || double.IsNaN(prefetch) || double.IsInfinity(prefetch)) prefetch = 4;
                s.PrefetchSeconds = prefetch;
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
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
        private delegate bool WindowCallback(IntPtr hwnd, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        private readonly Settings settings;
        private readonly Session session;
        private readonly string root;
        private readonly bool preview, reduced, cover;
        private readonly double skipAfter;
        private readonly MediaElement player;
        private readonly Grid videoHost;
        private readonly System.Windows.Shapes.Rectangle curtain;
        private readonly TextBlock status;
        private readonly Button skipButton;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Stopwatch handoff = new Stopwatch();
        private readonly Stopwatch dismiss = new Stopwatch();
        private readonly Stopwatch windowPoll = new Stopwatch();
        private readonly Stopwatch topClock = new Stopwatch();
        private System.Drawing.Rectangle monitorBounds;
        private bool boundsReady, started, finishing, cleaned, opened, revealed, fadeStarted, fadeDone, closing;
        private bool activationStarted, activationReused, holdForWindow, watchingEnd, lastVisible, skipQueued, toppedOverTarget, warmedTarget;
        private double duration = 32, position, progressAt;
        private int renderCount;
        private uint appPid;
        private IntPtr appWindow, keyHook;
        private string activationError;
        private LowLevelProc keyHookProc;
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;

        public Splash(Settings s, Session log, string folder, bool onlyPreview, bool reduce, double skip)
        {
            settings = s; session = log; root = folder; preview = onlyPreview; reduced = reduce; skipAfter = skip;
            cover = settings.VideoFit != "contain";
            session.Data["video_fit"] = settings.VideoFit;
            session.Data["prefetch_seconds"] = settings.PrefetchSeconds;
            Title = "Codex Industrial · 非官方启动动画";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Normal;
            AllowsTransparency = false; Background = Brushes.Black; ShowInTaskbar = false; Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual; Focusable = true; ShowActivated = true;
            PlaceOnMonitor();
            player = new MediaElement
            {
                LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
                Stretch = cover ? Stretch.UniformToFill : Stretch.Uniform,
                IsMuted = true, Volume = 0, ScrubbingEnabled = false, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(player, BitmapScalingMode.LowQuality);
            RenderOptions.SetEdgeMode(player, EdgeMode.Aliased);
            videoHost = new Grid { Background = Brushes.Black, ClipToBounds = true, UseLayoutRounding = true, SnapsToDevicePixels = true };
            videoHost.Children.Add(player);
            videoHost.SizeChanged += delegate { LayoutVideo(); };
            curtain = new System.Windows.Shapes.Rectangle { Fill = Brushes.Black, IsHitTestVisible = false, Opacity = 1 };
            status = new TextBlock
            {
                Text = "", Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Margin = new Thickness(28),
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false
            };
            skipButton = CreateSkipButton();
            var grid = new Grid { Background = Brushes.Black, UseLayoutRounding = true, ClipToBounds = true, SnapsToDevicePixels = true };
            grid.Children.Add(videoHost);
            grid.Children.Add(curtain);
            grid.Children.Add(status);
            grid.Children.Add(skipButton);
            Content = grid;
            PreviewKeyDown += OnPreviewKeyDown;
            player.MediaOpened += OnMediaOpened;
            player.MediaEnded += delegate { if (finishing) return; session.Data["completed"] = true; Finish("completed"); };
            player.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e) { Fail(e.ErrorException == null ? "视频无法解码。" : e.ErrorException.Message); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Tick;
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += delegate { if (!finishing) Finish("window_closed"); };
            Closed += OnClosed;
        }

        private Button CreateSkipButton()
        {
            var idle = new SolidColorBrush(Color.FromArgb(148, 255, 255, 255));
            var hot = Brushes.White;
            var hotFill = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var button = new Button
            {
                Content = "跳过",
                ToolTip = "跳过动画并打开 Codex（Esc / Enter / 空格）",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = idle, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(28),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.Hand, Focusable = false, FocusVisualStyle = null, Opacity = 0, Template = template
            };
            button.MouseEnter += delegate { button.Foreground = hot; button.Background = hotFill; };
            button.MouseLeave += delegate { button.Foreground = idle; button.Background = Brushes.Transparent; };
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; Finish("skipped"); };
            button.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(800) });
            return button;
        }

        private void PlaceOnMonitor()
        {
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            monitorBounds = screen.Bounds;
            boundsReady = monitorBounds.Width > 0 && monitorBounds.Height > 0;
            uint dpi = 96;
            try
            {
                var point = new POINT { X = monitorBounds.X + monitorBounds.Width / 2, Y = monitorBounds.Y + monitorBounds.Height / 2 };
                IntPtr monitor = MonitorFromPoint(point, 2);
                uint dpiX, dpiY;
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 && dpiX >= 96 && dpiX <= 480) dpi = dpiX;
            }
            catch { dpi = 96; }
            session.Data["monitor_dpi"] = dpi;
            session.Data["monitor_width"] = monitorBounds.Width;
            session.Data["monitor_height"] = monitorBounds.Height;
            double scale = dpi / 96.0;
            Left = monitorBounds.Left / scale;
            Top = monitorBounds.Top / scale;
            Width = Math.Max(1, monitorBounds.Width / scale);
            Height = Math.Max(1, monitorBounds.Height / scale);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(hwnd);
            if (source != null)
            {
                source.AddHook(WndProc);
                if (source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Black;
            }
            if (boundsReady) SetWindowPos(hwnd, new IntPtr(-1), monitorBounds.X, monitorBounds.Y, monitorBounds.Width, monitorBounds.Height, SwpShowWindow);
            SyncRootSize();
            InstallKeyHook();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (started) return;
            started = true;
            try { Activate(); Focus(); Keyboard.Focus(this); } catch { }
            clock.Start(); timer.Start();
            if (reduced)
            {
                status.Text = "Codex · 正在启动";
                if (!preview && EnsureActivated() && activationReused)
                    Dispatcher.BeginInvoke(new Action(delegate { Finish("reduced_motion"); }));
                return;
            }
            try
            {
                string video = Path.GetFullPath(Path.Combine(root, settings.Video));
                if (!File.Exists(video)) { Fail("启动视频不存在。"); return; }
                player.Source = new Uri(video);
                player.Play();
                CompositionTarget.Rendering += OnFrame;
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            cleaned = true;
            StopFrameWatch();
            timer.Stop();
            if (keyHook != IntPtr.Zero) { UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
            try { player.Close(); } catch { }
            if (handoff.IsRunning) session.Data["handoff_seconds"] = Math.Round(handoff.Elapsed.TotalSeconds, 3);
            if (dismiss.IsRunning) session.Data["dismiss_seconds"] = Math.Round(dismiss.Elapsed.TotalSeconds, 3);
            session.Save();
        }

        private void OnMediaOpened(object sender, RoutedEventArgs e)
        {
            if (finishing) return;
            opened = true; session.Data["media_opened"] = true;
            session.Data["video_width"] = player.NaturalVideoWidth; session.Data["video_height"] = player.NaturalVideoHeight;
            if (player.NaturalDuration.HasTimeSpan) duration = player.NaturalDuration.TimeSpan.TotalSeconds;
            session.Data["duration_seconds"] = duration;
            if (duration <= 0 || duration > 180 || double.IsNaN(duration)) { Fail("视频时长无效。"); return; }
            progressAt = clock.Elapsed.TotalSeconds;
            LayoutVideo();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat) { e.Handled = true; return; }
            if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; Finish("skipped"); }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (cleaned || finishing || revealed) { CompositionTarget.Rendering -= OnFrame; return; }
            if (!opened) return;
            renderCount++;
            double pos;
            try { pos = player.Position.TotalSeconds; } catch { return; }
            // Hide the decoder warmup. Revealing on the first advanced frame avoids a white/black pop and the hitchy opening.
            if (pos > 0.04 || renderCount >= 12)
            {
                CompositionTarget.Rendering -= OnFrame;
                Reveal();
            }
        }

        private void Reveal()
        {
            if (revealed || finishing || cleaned) return;
            revealed = true;
            session.Data["reveal_seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 3);
            LayoutVideo();
            curtain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void Tick(object sender, EventArgs e)
        {
            if (cleaned) return;
            if (finishing)
            {
                if (holdForWindow && !fadeStarted)
                {
                    bool visible = RefreshWindow(80);
                    if (visible)
                    {
                        session.Data["app_window_visible"] = true;
                        session.Data["handoff_immediate"] = true;
                        fadeDone = true;
                        TryComplete();
                        return;
                    }
                    if (dismiss.Elapsed.TotalSeconds > 2.5 && status.Text.Length == 0) status.Text = "正在进入 Codex…";
                    if (dismiss.Elapsed.TotalSeconds >= 8)
                    {
                        if (reduced) { fadeDone = true; TryComplete(); }
                        else BeginFade();
                    }
                    return;
                }
                if (fadeDone) TryComplete();
                return;
            }
            // Pin above a process we just launched, and only until its window appears or about a second has passed. An already-open Codex is not relaunched and is not fought.
            if (activationStarted && !activationReused && !toppedOverTarget)
            {
                if (handoff.IsRunning && handoff.Elapsed.TotalSeconds >= 1.2 && !lastVisible) toppedOverTarget = true;
                else
                {
                    bool wasVisible = lastVisible;
                    RefreshWindow(400);
                    if (!lastVisible) KeepOnTop();
                    else if (!wasVisible)
                    {
                        toppedOverTarget = true;
                        topClock.Reset();
                        KeepOnTop();
                    }
                }
            }
            double elapsed = clock.Elapsed.TotalSeconds;
            if (skipAfter >= 0 && elapsed >= skipAfter) { Finish("diagnostic_skip"); return; }
            if (reduced) { if (elapsed >= 0.6) Finish("reduced_motion"); return; }
            if (!opened) { if (elapsed > 10) Fail("视频打开超时。"); return; }
            double current;
            try { current = player.Position.TotalSeconds; } catch (Exception ex) { Fail(ex.Message); return; }
            if (current > position + 0.005) { position = current; progressAt = elapsed; }
            session.Data["max_position_seconds"] = Math.Round(position, 3);
            if (!watchingEnd && duration - position <= 1)
            {
                watchingEnd = true;
                timer.Interval = TimeSpan.FromMilliseconds(30);
            }
            if (!preview && settings.PrefetchSeconds > 0 && position >= Math.Max(0, duration - settings.PrefetchSeconds))
            {
                if (!activationStarted) session.Data["activation_prefetch"] = true;
                if (!EnsureActivated()) { Finish("activation_failed"); return; }
            }
            if (activationReused && !warmedTarget && duration - position <= 0.4)
            {
                warmedTarget = true;
                RefreshWindow(0);
            }
            // Stop just before MediaEnded blanks the picture. If Codex is already up, Finish switches immediately.
            if (position >= duration - 0.05) { session.Data["completed"] = true; Finish("completed"); return; }
            if (elapsed - progressAt > 6) Fail("视频播放停止超过 6 秒。");
            else if (elapsed > duration + 20) Fail("视频播放超时。");
        }

        private void LayoutVideo()
        {
            if (reduced) return;
            double vw = player.NaturalVideoWidth, vh = player.NaturalVideoHeight;
            double cw = videoHost.ActualWidth, ch = videoHost.ActualHeight;
            if (vw < 2 || vh < 2 || cw < 2 || ch < 2) return;
            double scale = cover ? Math.Max(cw / vw, ch / vh) : Math.Min(cw / vw, ch / vh);
            double dpi = 1;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                double m = source.CompositionTarget.TransformToDevice.M11;
                if (m > 0.5 && m < 8) dpi = m;
            }
            double pxW = vw * scale * dpi, pxH = vh * scale * dpi;
            pxW = cover ? Math.Ceiling(pxW) : Math.Floor(pxW);
            pxH = cover ? Math.Ceiling(pxH) : Math.Floor(pxH);
            player.Width = Math.Max(1, pxW / dpi);
            player.Height = Math.Max(1, pxH / dpi);
            player.Stretch = Stretch.Fill;
        }

        private void SyncRootSize()
        {
            if (!boundsReady) return;
            double dpi = 1;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                double m = source.CompositionTarget.TransformToDevice.M11;
                if (m > 0.5 && m < 8) dpi = m;
            }
            double w = monitorBounds.Width / dpi, h = monitorBounds.Height / dpi;
            if (Math.Abs(Width - w) > 0.5) Width = w;
            if (Math.Abs(Height - h) > 0.5) Height = h;
        }

        private bool RecentVisibleTarget()
        {
            return lastVisible && appWindow != IntPtr.Zero && windowPoll.IsRunning && windowPoll.ElapsedMilliseconds < 500;
        }

        private void RememberReusedTarget()
        {
            activationStarted = true;
            activationReused = true;
            session.Data["activation_attempted"] = true;
            session.Data["activation_succeeded"] = true;
            session.Data["activation_reused"] = true;
            session.Data["app_pid"] = appPid;
        }

        private bool EnsureActivated()
        {
            if (preview) return false;
            if (activationStarted && !activationReused) return activationError == null;
            if (RecentVisibleTarget()) { RememberReusedTarget(); return true; }
            if (RefreshWindow(0)) { RememberReusedTarget(); return true; }
            activationReused = false;
            appWindow = IntPtr.Zero;
            lastVisible = false;
            activationStarted = true;
            session.Data["activation_attempted"] = true;
            session.Data["activation_reused"] = false;
            try
            {
                appPid = Activation.Open(settings, root);
                session.Data["activation_succeeded"] = true;
                session.Data["app_pid"] = appPid;
                if (!handoff.IsRunning) handoff.Start();
                return true;
            }
            catch (Exception e)
            {
                activationError = e.Message;
                session.Data["activation_error"] = e.Message;
                session.Data["activation_succeeded"] = false;
                return false;
            }
        }

        private bool RefreshWindow(int minIntervalMs)
        {
            if (preview) return false;
            if (minIntervalMs > 0 && windowPoll.IsRunning && windowPoll.ElapsedMilliseconds < minIntervalMs) return lastVisible;
            windowPoll.Restart();
            lastVisible = FindApplicationWindow();
            return lastVisible;
        }

        private bool FindApplicationWindow()
        {
            IntPtr best = IntPtr.Zero;
            uint bestPid = 0;
            int bestArea = 0;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (!IsWindowVisible(window)) return true;
                uint pid; GetWindowThreadProcessId(window, out pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return true;
                try
                {
                    using (var process = Process.GetProcessById((int)pid))
                    {
                        bool match = appPid != 0 && pid == appPid;
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
                    RECT rect;
                    int area = 1;
                    if (GetWindowRect(window, out rect))
                    {
                        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
                        if (w > 0 && h > 0) area = w * h;
                    }
                    // Prefer a real window over a cloaked host of the same app. A cloaked-only match is still reused so we don't launch a second process.
                    int score = area + (IsCloaked(window) ? 0 : 1000000000);
                    if (score >= bestArea)
                    {
                        bestArea = score; best = window; bestPid = pid;
                    }
                    return true;
                }
                catch { return true; }
            }, IntPtr.Zero);
            if (best == IntPtr.Zero) return false;
            appWindow = best;
            if (appPid == 0) appPid = bestPid;
            session.Data["visible_app_pid"] = bestPid;
            return true;
        }

        private static bool IsCloaked(IntPtr window)
        {
            int cloaked;
            try { return DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0; }
            catch { return false; }
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
            finishing = true;
            session.Data["reason"] = reason;
            StopFrameWatch();
            try { player.Pause(); } catch { }
            if (reason == "window_closed")
            {
                if (!preview) EnsureActivated();
                if (activationError != null) ShowActivationError();
                session.Save();
                return;
            }
            if (preview)
            {
                if (!dismiss.IsRunning) dismiss.Start();
                if (reason == "reduced_motion") { Dispatcher.BeginInvoke(new Action(Close)); return; }
                BeginFade();
                return;
            }
            if (!EnsureActivated())
            {
                session.Save();
                ShowActivationError();
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }
            if (!dismiss.IsRunning) dismiss.Start();
            if (!handoff.IsRunning) handoff.Start();
            // Already on screen: cut straight across. A black fade or a held frame reads as lag when Codex is waiting.
            if (RecentVisibleTarget() || RefreshWindow(0))
            {
                session.Data["app_window_visible"] = true;
                session.Data["handoff_immediate"] = true;
                fadeDone = true;
                TryComplete();
                return;
            }
            if (reason == "completed" && opened)
            {
                holdForWindow = true;
                return;
            }
            BeginFade();
        }

        private void ShowActivationError()
        {
            closing = true;
            DropTopmost();
            try { if (IsVisible) Hide(); } catch { }
            string message = activationError == null ? "无法打开 Codex。" : activationError;
            MessageBox.Show("无法打开 Codex，请先确认桌面版已安装。\n\n" + message, "Codex Industrial", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void BeginFade()
        {
            if (fadeStarted || cleaned) return;
            fadeStarted = true;
            if (!dismiss.IsRunning) dismiss.Start();
            var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += delegate { fadeDone = true; TryComplete(); };
            curtain.BeginAnimation(OpacityProperty, anim);
            skipButton.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        }

        private void TryComplete()
        {
            if (!fadeDone || cleaned || closing) return;
            if (!preview)
            {
                bool visible = RecentVisibleTarget() || RefreshWindow(0);
                session.Data["app_window_visible"] = visible;
                bool timedOut = !dismiss.IsRunning || dismiss.Elapsed.TotalSeconds >= 8;
                if (!visible && !timedOut) return;
                closing = true;
                DropTopmost();
                if (visible) ForceForeground(appWindow);
            }
            else closing = true;
            Close();
        }

        private void KeepOnTop()
        {
            if (closing || cleaned) return;
            if (topClock.IsRunning && topClock.ElapsedMilliseconds < 200) return;
            topClock.Restart();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private void DropTopmost()
        {
            Topmost = false;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        private static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            uint unused;
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out unused);
            uint ourThread = GetCurrentThreadId();
            bool attached = false;
            if (foregroundThread != 0 && foregroundThread != ourThread) attached = AttachThreadInput(ourThread, foregroundThread, true);
            ShowWindow(hwnd, IsIconic(hwnd) ? 9 : 5);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(ourThread, foregroundThread, false);
        }

        private void StopFrameWatch()
        {
            CompositionTarget.Rendering -= OnFrame;
        }

        private void InstallKeyHook()
        {
            try
            {
                keyHookProc = KeyHook;
                string module = Process.GetCurrentProcess().MainModule.ModuleName;
                keyHook = SetWindowsHookEx(13, keyHookProc, GetModuleHandle(module), 0);
            }
            catch { keyHook = IntPtr.Zero; }
        }

        private IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == 0 && !finishing && !cleaned && lParam != IntPtr.Zero)
                {
                    int message = wParam.ToInt32();
                    if (message == 0x0100 || message == 0x0104)
                    {
                        int vk = Marshal.ReadInt32(lParam);
                        if (!skipQueued && (vk == 0x1B || vk == 0x0D || vk == 0x20) && ShouldTakeSkipKey())
                        {
                            skipQueued = true;
                            try { Dispatcher.BeginInvoke(new Action(delegate { Finish("skipped"); })); }
                            catch { skipQueued = false; return CallNextHookEx(keyHook, code, wParam, lParam); }
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch { /* Never stall the system keyboard hook. */ }
            return CallNextHookEx(keyHook, code, wParam, lParam);
        }

        private bool ShouldTakeSkipKey()
        {
            if (!IsVisible) return false;
            IntPtr foreground = GetForegroundWindow();
            IntPtr ours = new WindowInteropHelper(this).Handle;
            if (foreground == IntPtr.Zero || foreground == ours) return true;
            RECT rect;
            if (!GetWindowRect(foreground, out rect) || !boundsReady) return false;
            int width = Math.Max(1, rect.Right - rect.Left), height = Math.Max(1, rect.Bottom - rect.Top);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rect.Left + width / 2, rect.Top + height / 2));
            var oursScreen = System.Windows.Forms.Screen.FromRectangle(monitorBounds);
            return string.Equals(screen.DeviceName, oursScreen.DeviceName, StringComparison.OrdinalIgnoreCase);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0014)
            {
                RECT rect;
                if (GetClientRect(hwnd, out rect))
                {
                    IntPtr brush = CreateSolidBrush(0);
                    if (brush != IntPtr.Zero)
                    {
                        FillRect(wParam, ref rect, brush);
                        DeleteObject(brush);
                    }
                }
                handled = true;
                return (IntPtr)1;
            }
            if (msg == 0x0046 && boundsReady && !closing)
            {
                var pos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                bool move = (pos.flags & SwpNoMove) == 0;
                bool size = (pos.flags & SwpNoSize) == 0;
                bool posOff = move && (Math.Abs(pos.x - monitorBounds.X) > 2 || Math.Abs(pos.y - monitorBounds.Y) > 2);
                bool sizeOff = size && (Math.Abs(pos.cx - monitorBounds.Width) > 2 || Math.Abs(pos.cy - monitorBounds.Height) > 2);
                if (posOff || sizeOff)
                {
                    pos.x = monitorBounds.X;
                    pos.y = monitorBounds.Y;
                    pos.cx = monitorBounds.Width;
                    pos.cy = monitorBounds.Height;
                    pos.flags &= ~(SwpNoSize | SwpNoMove);
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (msg == 0x02E0)
            {
                Dispatcher.BeginInvoke(new Action(delegate { SyncRootSize(); LayoutVideo(); }));
            }
            return IntPtr.Zero;
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
                    var latency = GCSettings.LatencyMode;
                    GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
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
                    finally
                    {
                        try { GCSettings.LatencyMode = latency; } catch { }
                        mutex.ReleaseMutex();
                    }
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
