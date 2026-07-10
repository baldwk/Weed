using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Weed.Abstractions;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using WpfButton = System.Windows.Controls.Button;

namespace Weed.Platform.Windows;

public sealed class WindowsClipboardService : IWeedClipboard
{
    private static readonly TimeSpan[] ClipboardRetryDelays =
    [
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(140),
        TimeSpan.FromMilliseconds(220),
        TimeSpan.FromMilliseconds(320)
    ];

    private readonly IWeedLogger? _logger;

    public WindowsClipboardService(IWeedLogger? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<ClipboardSnapshot?> TryReadAsync(CancellationToken cancellationToken)
    {
        return await InvokeOnDispatcherAsync(() =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray();
                    if (files.Length > 0)
                    {
                        return new ClipboardSnapshot
                        {
                            Kind = ClipboardContentKind.Files,
                            Files = files,
                            TextContent = string.Join(Environment.NewLine, files)
                        };
                    }
                }

                if (System.Windows.Clipboard.ContainsImage())
                {
                    var image = System.Windows.Clipboard.GetImage();
                    if (image is not null)
                    {
                        return new ClipboardSnapshot
                        {
                            Kind = ClipboardContentKind.Image,
                            ImagePng = EncodePng(image),
                            TextContent = $"Image {image.PixelWidth} x {image.PixelHeight}"
                        };
                    }
                }

                if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Html))
                {
                    var html = System.Windows.Clipboard.GetData(System.Windows.DataFormats.Html)?.ToString();
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        return new ClipboardSnapshot
                        {
                            Kind = ClipboardContentKind.Html,
                            Html = html,
                            TextContent = System.Windows.Clipboard.ContainsText()
                                ? System.Windows.Clipboard.GetText()
                                : StripHtml(html)
                        };
                    }
                }

                if (System.Windows.Clipboard.ContainsData(System.Windows.DataFormats.Rtf))
                {
                    var rtf = System.Windows.Clipboard.GetData(System.Windows.DataFormats.Rtf)?.ToString();
                    if (!string.IsNullOrWhiteSpace(rtf))
                    {
                        return new ClipboardSnapshot
                        {
                            Kind = ClipboardContentKind.Rtf,
                            Rtf = rtf,
                            TextContent = System.Windows.Clipboard.ContainsText()
                                ? System.Windows.Clipboard.GetText()
                                : "Rich text"
                        };
                    }
                }

                if (System.Windows.Clipboard.ContainsText())
                {
                    return new ClipboardSnapshot
                    {
                        Kind = ClipboardContentKind.Text,
                        TextContent = System.Windows.Clipboard.GetText()
                    };
                }
            }
            catch
            {
                return null;
            }

            return null;
        }, cancellationToken);
    }

    public async ValueTask<string?> TryGetTextAsync(CancellationToken cancellationToken)
    {
        return await InvokeOnDispatcherAsync(() =>
        {
            try
            {
                return System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
            }
            catch
            {
                return null;
            }
        }, cancellationToken);
    }

    public async ValueTask SetTextAsync(string text, CancellationToken cancellationToken)
    {
        await SetClipboardWithRetryAsync(
            () => System.Windows.Clipboard.SetText(text),
            "set text",
            cancellationToken);
    }

    public async ValueTask SetFilesAsync(IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        await SetClipboardWithRetryAsync(
            () =>
            {
                var collection = new StringCollection();
                collection.AddRange(files.ToArray());
                var data = new System.Windows.DataObject();
                data.SetFileDropList(collection);
                System.Windows.Clipboard.SetDataObject(data, true);
            },
            "set files",
            cancellationToken);
    }

    public async ValueTask PasteTextAsync(string text, CancellationToken cancellationToken)
    {
        await SetTextAsync(text, cancellationToken);
        await PasteCurrentAsync(cancellationToken);
    }

    public async ValueTask PasteCurrentAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(80, cancellationToken);
        Forms.SendKeys.SendWait("^v");
    }

    public async ValueTask SetImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        var image = await InvokeOnDispatcherAsync(() =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(imagePath);
            image.EndInit();
            image.Freeze();
            return (BitmapSource)image;
        }, cancellationToken);

        await SetClipboardWithRetryAsync(
            () => System.Windows.Clipboard.SetImage(image),
            "set image",
            cancellationToken);
    }

    private async ValueTask SetClipboardWithRetryAsync(
        Action action,
        string operation,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var lastOwner = "unknown";
        for (var attempt = 0; attempt <= ClipboardRetryDelays.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await InvokeOnDispatcherAsync(() =>
                {
                    action();
                    return true;
                }, cancellationToken);

                if (attempt > 0)
                {
                    _logger?.Info($"Clipboard {operation} succeeded after {attempt + 1} attempts. Last busy owner: {lastOwner}");
                }

                return;
            }
            catch (Exception ex) when (IsClipboardUnavailable(ex) && attempt < ClipboardRetryDelays.Length)
            {
                lastError = ex;
                lastOwner = DescribeOpenClipboardOwner();
                await Task.Delay(ClipboardRetryDelays[attempt], cancellationToken);
            }
        }

        if (lastError is not null)
        {
            _logger?.Warn($"Clipboard {operation} failed because the clipboard is busy: {lastError.Message}. Last busy owner: {lastOwner}");
            throw lastError;
        }
    }

    private static bool IsClipboardUnavailable(Exception exception) =>
        exception is COMException { ErrorCode: unchecked((int)0x800401D0) } ||
        exception is ExternalException { ErrorCode: unchecked((int)0x800401D0) };

    private static string DescribeOpenClipboardOwner()
    {
        var hwnd = GetOpenClipboardWindow();
        if (hwnd == IntPtr.Zero)
        {
            return "no open clipboard window";
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        var process = ProcessName(processId);
        var windowTitle = title.Length == 0 ? "untitled window" : title.ToString();
        return $"{process}, hwnd 0x{hwnd.ToInt64():X}, title \"{windowTitle}\"";
    }

    private static string ProcessName(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return $"pid {processId}";
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return $"{process.ProcessName} (pid {processId})";
        }
        catch
        {
            return $"pid {processId}";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string StripHtml(string html)
    {
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static async ValueTask<T> InvokeOnDispatcherAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        var operation = dispatcher.InvokeAsync(action);
        await operation.Task.WaitAsync(cancellationToken);
        return operation.Result;
    }
}

public sealed class WindowsShellService : IWeedShell
{
    private readonly IWeedClipboard _clipboard;

    public WindowsShellService(IWeedClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public ValueTask OpenAsync(string pathOrUri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pathOrUri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", pathOrUri)
            {
                UseShellExecute = true
            });
            return ValueTask.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(pathOrUri)
        {
            UseShellExecute = true
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenAsAdministratorAsync(string path, string? arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(path)
        {
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = true,
            Verb = "runas"
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenContainingFolderAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        else if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask CopyPathAsync(string path, CancellationToken cancellationToken)
    {
        await _clipboard.SetTextAsync(path, cancellationToken);
    }
}

public sealed class WindowsScreenCaptureService : IWeedScreenCapture
{
    private const int MouseeventfWheel = 0x0800;
    private const string ScreenshotPluginId = "weed.screenshot";
    private readonly string _defaultScreenshotDirectory;
    private readonly IWeedSettings? _settings;

    public WindowsScreenCaptureService(string screenshotDirectory, IWeedSettings? settings = null)
    {
        _defaultScreenshotDirectory = screenshotDirectory;
        _settings = settings;
        Directory.CreateDirectory(_defaultScreenshotDirectory);
    }

    public async ValueTask<ScreenCaptureResult?> CaptureRegionRawAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = await RegionCaptureOverlay.SelectRegionAsync(cancellationToken);
        if (bounds is null || bounds.Value.Width <= 2 || bounds.Value.Height <= 2)
        {
            return null;
        }

        return Capture(bounds.Value);
    }

    public async ValueTask<ScreenCaptureResult?> CaptureRegionInteractiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var snapshot = CaptureVirtualDesktop("Screenshot");
        var windowTargets = EnumerateScreenshotWindowTargets(virtualScreen);
        return await ScreenshotSnapshotOverlay.CaptureRegionAndEditAsync(
            snapshot,
            virtualScreen,
            windowTargets,
            DefaultAnnotationColor(),
            DefaultLineWidth(),
            JpegQuality(),
            MaxSavedFileBytes(),
            SaveDefaultAnnotationColor,
            cancellationToken);
    }

    public async ValueTask<ScreenCaptureResult?> CapturePrimaryScreenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? Forms.Screen.AllScreens.First().Bounds;
        return await ScreenshotSnapshotOverlay.EditImageAsync(
            Capture(bounds),
            DefaultAnnotationColor(),
            DefaultLineWidth(),
            JpegQuality(),
            MaxSavedFileBytes(),
            SaveDefaultAnnotationColor,
            cancellationToken);
    }

    public async ValueTask<ScreenCaptureResult?> CaptureScrollingInteractiveAsync(CancellationToken cancellationToken)
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var snapshot = CaptureVirtualDesktop("Screenshot-scroll");
        var localBounds = await ScreenshotSnapshotOverlay.SelectRegionAsync(
            snapshot,
            virtualScreen,
            EnumerateScreenshotWindowTargets(virtualScreen),
            cancellationToken);
        if (localBounds is null || localBounds.Value.Width <= 2 || localBounds.Value.Height <= 2)
        {
            return null;
        }

        var bounds = new Rectangle(
            virtualScreen.Left + localBounds.Value.Left,
            virtualScreen.Top + localBounds.Value.Top,
            localBounds.Value.Width,
            localBounds.Value.Height);
        var options = await ScrollingCaptureOptionsWindow.GetOptionsAsync(cancellationToken);
        if (options is null)
        {
            return null;
        }

        await Task.Delay(250, cancellationToken);
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = ScrollingCaptureProgressWindow.Show(captureCts.Cancel);
        try
        {
            var stitched = await Task.Run(
                () => CaptureScrolling(bounds, options.Value, progress.Report, captureCts.Token),
                captureCts.Token);
            progress.CloseSafely();
            return await ScreenshotSnapshotOverlay.EditImageAsync(
                stitched,
                DefaultAnnotationColor(),
                DefaultLineWidth(),
                JpegQuality(),
                MaxSavedFileBytes(),
                SaveDefaultAnnotationColor,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress.CloseSafely();
            return null;
        }
    }

    private ScreenCaptureResult Capture(Rectangle bounds)
    {
        using var bitmap = CaptureBitmap(bounds);
        return new ScreenCaptureResult
        {
            FilePath = SuggestedScreenshotPath("Screenshot"),
            ImagePng = EncodePng(bitmap),
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private ScreenCaptureResult CaptureVirtualDesktop(string prefix)
    {
        var bounds = Forms.SystemInformation.VirtualScreen;
        using var bitmap = CaptureBitmap(bounds);
        return new ScreenCaptureResult
        {
            FilePath = SuggestedScreenshotPath(prefix),
            ImagePng = EncodePng(bitmap),
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private ScreenCaptureResult CaptureScrolling(
        Rectangle bounds,
        ScrollingCaptureOptions options,
        Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        var frames = new List<Bitmap>();
        try
        {
            for (var i = 0; i < options.FrameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames.Add(CaptureBitmap(bounds));
                progress?.Invoke(i + 1, options.FrameCount);
                if (i < options.FrameCount - 1)
                {
                    ScrollAt(bounds, options.WheelNotches);
                    Thread.Sleep(options.DelayMs);
                }
            }

            using var stitched = new Bitmap(bounds.Width, bounds.Height * frames.Count, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(stitched))
            {
                graphics.Clear(System.Drawing.Color.White);
                var y = 0;
                foreach (var frame in frames)
                {
                    graphics.DrawImageUnscaled(frame, 0, y);
                    y += frame.Height;
                }
            }

            return new ScreenCaptureResult
            {
                FilePath = SuggestedScreenshotPath("Screenshot-scroll"),
                ImagePng = EncodePng(stitched),
                Width = stitched.Width,
                Height = stitched.Height
            };
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private string SuggestedScreenshotPath(string prefix) =>
        Path.Combine(ScreenshotDirectory(), $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.{DefaultImageExtension()}");

    private string ScreenshotDirectory()
    {
        var configured = _settings?.GetPluginSetting(ScreenshotPluginId, "defaultSaveDirectory", string.Empty);
        var path = string.IsNullOrWhiteSpace(configured) ? _defaultScreenshotDirectory : Environment.ExpandEnvironmentVariables(configured);
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            Directory.CreateDirectory(_defaultScreenshotDirectory);
            return _defaultScreenshotDirectory;
        }
    }

    private string DefaultAnnotationColor() =>
        _settings?.GetPluginSetting(ScreenshotPluginId, "defaultColor", "Red") ?? "Red";

    private void SaveDefaultAnnotationColor(string color) =>
        _settings?.SetPluginSetting(ScreenshotPluginId, "defaultColor", color);

    private double DefaultLineWidth() =>
        Math.Clamp(_settings?.GetPluginSetting(ScreenshotPluginId, "defaultLineWidth", 4) ?? 4, 2, 18);

    private int JpegQuality() =>
        Math.Clamp(_settings?.GetPluginSetting(ScreenshotPluginId, "jpegQuality", 90) ?? 90, 1, 100);

    private long MaxSavedFileBytes() =>
        Math.Clamp(_settings?.GetPluginSetting(ScreenshotPluginId, "maxSavedFileMegabytes", 2) ?? 2, 1, 100) * 1024L * 1024L;

    private string DefaultImageExtension()
    {
        var format = _settings?.GetPluginSetting(ScreenshotPluginId, "defaultFormat", "png") ?? "png";
        return format.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";
    }

    private static Bitmap CaptureBitmap(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static IReadOnlyList<ScreenshotWindowTarget> EnumerateScreenshotWindowTargets(Rectangle virtualScreenBounds) =>
        ScreenshotOverlayLayout.WindowTargetsFromScreenBounds(
            ScreenshotWindowEnumerator.EnumerateVisibleWindowBounds(),
            virtualScreenBounds);

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void ScrollAt(Rectangle bounds, int wheelNotches)
    {
        var x = bounds.Left + bounds.Width / 2;
        var y = bounds.Top + bounds.Height / 2;
        SetCursorPos(x, y);
        mouse_event(MouseeventfWheel, 0, 0, unchecked((uint)(-120 * wheelNotches)), UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}

public sealed record ScreenshotWindowTarget(Rectangle PixelBounds);

internal static class ScreenshotWindowEnumerator
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

    public static IReadOnlyList<Rectangle> EnumerateVisibleWindowBounds()
    {
        var bounds = new List<Rectangle>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidateWindow(hwnd) || !TryGetWindowBounds(hwnd, out var windowBounds))
            {
                return true;
            }

            if (windowBounds.Width > 2 && windowBounds.Height > 2)
            {
                bounds.Add(windowBounds);
            }

            return true;
        }, IntPtr.Zero);
        return bounds;
    }

    private static bool IsCandidateWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero ||
            !IsWindowVisible(hwnd) ||
            IsIconic(hwnd) ||
            IsCloaked(hwnd))
        {
            return false;
        }

        var className = GetClassNameText(hwnd);
        return !className.Equals("Progman", StringComparison.OrdinalIgnoreCase) &&
               !className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) &&
               !className.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(
                hwnd,
                DwmwaExtendedFrameBounds,
                out var extendedBounds,
                Marshal.SizeOf<NativeRect>()) == 0)
        {
            bounds = extendedBounds.ToRectangle();
        }
        else if (GetWindowRect(hwnd, out var windowBounds))
        {
            bounds = windowBounds.ToRectangle();
        }

        return !bounds.IsEmpty && bounds.Width > 2 && bounds.Height > 2;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttributeInt(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string GetClassNameText(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out NativeRect pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute", PreserveSig = true)]
    private static extern int DwmGetWindowAttributeInt(
        IntPtr hwnd,
        int dwAttribute,
        out int pvAttribute,
        int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Math.Max(Left, Right), Math.Max(Top, Bottom));
    }
}

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int WhKeyboardLl = 13;
    private readonly Dictionary<int, string> _commands = [];
    private readonly List<KeyboardHookRegistration> _fallbackHotkeys = [];
    private HwndSource? _source;
    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookHandle;
    private int _nextId = 100;

    public event EventHandler<string>? HotkeyPressed;

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var handle = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    public bool Register(Window window, string keys, string command)
    {
        if (_source is null)
        {
            Attach(window);
        }

        var helper = new WindowInteropHelper(window);
        var handle = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;
        if (!TryParse(keys, out var modifiers, out var virtualKey))
        {
            return false;
        }

        var id = _nextId++;
        if (!RegisterHotKey(handle, id, modifiers, virtualKey))
        {
            _fallbackHotkeys.Add(new KeyboardHookRegistration(modifiers, virtualKey, command));
            EnsureFallbackHook();
            return _hookHandle != IntPtr.Zero;
        }

        _commands[id] = command;
        return true;
    }

    public void Clear()
    {
        if (_source is not null)
        {
            foreach (var id in _commands.Keys.ToArray())
            {
                UnregisterHotKey(_source.Handle, id);
            }
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _commands.Clear();
        _fallbackHotkeys.Clear();
    }

    public void Dispose()
    {
        Clear();
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _commands.TryGetValue(wParam.ToInt32(), out var command))
        {
            HotkeyPressed?.Invoke(this, command);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void EnsureFallbackHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookProc = KeyboardHookCallback;
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == WmKeydown || wParam.ToInt32() == WmSyskeydown))
        {
            var virtualKey = (uint)Marshal.ReadInt32(lParam);
            var modifiers = CurrentModifiers();
            var match = _fallbackHotkeys.FirstOrDefault(h => h.VirtualKey == virtualKey && h.Modifiers == modifiers);
            if (match is not null)
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null)
                {
                    dispatcher.BeginInvoke(() => HotkeyPressed?.Invoke(this, match.Command));
                }
                else
                {
                    HotkeyPressed?.Invoke(this, match.Command);
                }

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool TryParse(string keys, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        foreach (var part in NormalizeHotkey(keys).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0002;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0004;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0001;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= 0x0008;
            }
            else
            {
                virtualKey = (uint)KeyInterop.VirtualKeyFromKey(ParseKey(part));
            }
        }

        return virtualKey != 0;
    }

    private static string NormalizeHotkey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var part in parts)
        {
            var normalized = part.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : part;
            if (normalized.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Win", StringComparison.OrdinalIgnoreCase))
            {
                modifiers.Add(normalized);
            }
            else
            {
                key = normalized.Length == 1 ? normalized.ToUpperInvariant() : normalized;
            }
        }

        var ordered = new List<string>();
        foreach (var modifier in new[] { "Ctrl", "Shift", "Alt", "Win" })
        {
            if (modifiers.Contains(modifier))
            {
                ordered.Add(modifier);
            }
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            ordered.Add(key);
        }

        return string.Join("+", ordered);
    }

    private static Key ParseKey(string part)
    {
        if (part.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Space;
        }

        if (part.Length == 1 && char.IsLetter(part[0]))
        {
            return Enum.Parse<Key>(part.ToUpperInvariant());
        }

        if (part.Length == 1 && char.IsDigit(part[0]))
        {
            return Enum.Parse<Key>($"D{part}");
        }

        return Enum.TryParse<Key>(part, true, out var key) ? key : Key.None;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static uint CurrentModifiers()
    {
        uint modifiers = 0;
        if (IsKeyDown(0x11))
        {
            modifiers |= 0x0002;
        }

        if (IsKeyDown(0x10))
        {
            modifiers |= 0x0004;
        }

        if (IsKeyDown(0x12))
        {
            modifiers |= 0x0001;
        }

        if (IsKeyDown(0x5B) || IsKeyDown(0x5C))
        {
            modifiers |= 0x0008;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private sealed record KeyboardHookRegistration(uint Modifiers, uint VirtualKey, string Command);
}

internal sealed class RegionCaptureOverlay : Window
{
    private readonly CanvasGeometry _geometry = new();
    private Point? _start;
    private Rectangle? _result;

    private RegionCaptureOverlay()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = System.Windows.Input.Cursors.Cross;

        var virtualBounds = GetVirtualScreenBoundsDip();
        Left = virtualBounds.Left;
        Top = virtualBounds.Top;
        Width = virtualBounds.Width;
        Height = virtualBounds.Height;

        Content = _geometry;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public static async ValueTask<Rectangle?> SelectRegionAsync(CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        return await dispatcher.InvokeAsync(() =>
        {
            var overlay = new RegionCaptureOverlay();
            cancellationToken.Register(() => overlay.Dispatcher.Invoke(() =>
            {
                overlay.DialogResult = false;
                overlay.Close();
            }));
            overlay.ShowDialog();
            return overlay._result;
        }).Task.WaitAsync(cancellationToken);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _start = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_start is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        _geometry.Selection = ToRect(_start.Value, current);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_start is null)
        {
            return;
        }

        ReleaseMouseCapture();
        var selected = ToRect(_start.Value, e.GetPosition(this));
        _result = ScreenshotOverlayLayout.DeviceRectangleFromPoints(
            PointToScreen(new Point(selected.Left, selected.Top)),
            PointToScreen(new Point(selected.Right, selected.Bottom)));
        DialogResult = true;
        Close();
    }

    private static Rect ToRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Rect GetVirtualScreenBoundsDip() =>
        new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
}

internal static class ScreenshotSelectionStyle
{
    public static readonly System.Windows.Media.Brush BorderBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 145, 255));

    public const double BorderThickness = 4;
}

internal sealed class CanvasGeometry : FrameworkElement
{
    private Rect _selection;

    public Rect Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(84, 0, 0, 0)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Selection.Width <= 0 || Selection.Height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, Selection);
        drawingContext.DrawRectangle(null, new System.Windows.Media.Pen(ScreenshotSelectionStyle.BorderBrush, ScreenshotSelectionStyle.BorderThickness), Selection);
        var text = new FormattedText(
            $"{(int)Selection.Width} x {(int)Selection.Height}",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            System.Windows.Media.Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(Selection.Right + 8, Selection.Bottom + 8));
    }
}

internal readonly record struct ScrollingCaptureOptions(int FrameCount, int WheelNotches, int DelayMs);

internal sealed class ScrollingCaptureOptionsWindow : Window
{
    private readonly Slider _frames = new();
    private readonly Slider _notches = new();
    private readonly Slider _delay = new();

    private ScrollingCaptureOptionsWindow()
    {
        Title = "Scrolling Capture";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 27, 34));
        Foreground = System.Windows.Media.Brushes.White;
        Content = BuildContent();
    }

    public ScrollingCaptureOptions? Result { get; private set; }

    public static async ValueTask<ScrollingCaptureOptions?> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return new ScrollingCaptureOptions(5, 5, 360);
        }

        return await dispatcher.InvokeAsync(() =>
        {
            var window = new ScrollingCaptureOptionsWindow();
            cancellationToken.Register(() => window.Dispatcher.Invoke(() =>
            {
                window.DialogResult = false;
                window.Close();
            }));
            window.ShowDialog();
            return window.Result;
        }).Task.WaitAsync(cancellationToken);
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(14)
        };

        root.Children.Add(new TextBlock
        {
            Text = "Select capture settings, then focus the target window.",
            Margin = new Thickness(0, 0, 0, 12)
        });

        ConfigureSlider(_frames, 2, 8, 5, 1);
        root.Children.Add(LabeledSlider("Frames", _frames));

        ConfigureSlider(_notches, 1, 10, 5, 1);
        root.Children.Add(LabeledSlider("Scroll amount", _notches));

        ConfigureSlider(_delay, 150, 900, 360, 50);
        root.Children.Add(LabeledSlider("Delay ms", _delay));

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(Button("Start", (_, _) =>
        {
            Result = new ScrollingCaptureOptions(
                (int)Math.Round(_frames.Value),
                (int)Math.Round(_notches.Value),
                (int)Math.Round(_delay.Value));
            DialogResult = true;
            Close();
        }));
        buttons.Children.Add(Button("Cancel", (_, _) =>
        {
            DialogResult = false;
            Close();
        }));
        root.Children.Add(buttons);

        return root;
    }

    private static void ConfigureSlider(Slider slider, double min, double max, double value, double tick)
    {
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = value;
        slider.TickFrequency = tick;
        slider.IsSnapToTickEnabled = true;
        slider.Width = 190;
    }

    private static UIElement LabeledSlider(string label, Slider slider)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        return row;
    }

    private static WpfButton Button(string text, RoutedEventHandler onClick)
    {
        var button = new WpfButton
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5)
        };
        button.Click += onClick;
        return button;
    }
}

internal sealed class ScrollingCaptureProgressWindow : Window
{
    private readonly System.Windows.Controls.ProgressBar _progress = new();
    private readonly TextBlock _status = new();

    private ScrollingCaptureProgressWindow(Action cancel)
    {
        Title = "Scrolling Capture";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        MinHeight = 170;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 29, 38));
        Foreground = System.Windows.Media.Brushes.White;
        Content = BuildContent(cancel);
    }

    public static ScrollingCaptureProgressWindow Show(Action cancel)
    {
        var window = new ScrollingCaptureProgressWindow(cancel);
        window.Show();
        return window;
    }

    public void Report(int current, int total)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _progress.Maximum = Math.Max(1, total);
            _progress.Value = Math.Clamp(current, 0, Math.Max(1, total));
            _status.Text = $"Capturing frame {current} of {total}";
        });
    }

    public void CloseSafely()
    {
        Dispatcher.BeginInvoke(Close);
    }

    private UIElement BuildContent(Action cancel)
    {
        var root = new StackPanel
        {
            Margin = new Thickness(18)
        };
        _status.Text = "Preparing capture";
        _status.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_status);

        _progress.Height = 16;
        _progress.Minimum = 0;
        _progress.Maximum = 1;
        _progress.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_progress);

        var stop = new WpfButton
        {
            Content = "Stop",
            Width = 86,
            MinHeight = 34,
            Padding = new Thickness(12, 5, 12, 5),
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        stop.Click += (_, _) =>
        {
            cancel();
            stop.IsEnabled = false;
            _status.Text = "Stopping";
        };
        root.Children.Add(stop);
        return root;
    }
}

public static class ScreenshotOverlayLayout
{
    public static Rectangle DeviceRectangleFromPoints(Point first, Point second)
    {
        var left = (int)Math.Floor(Math.Min(first.X, second.X));
        var top = (int)Math.Floor(Math.Min(first.Y, second.Y));
        var right = (int)Math.Ceiling(Math.Max(first.X, second.X));
        var bottom = (int)Math.Ceiling(Math.Max(first.Y, second.Y));
        return Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    public static Rect DeviceRectToDipRect(Rectangle deviceRect, double dpiScaleX, double dpiScaleY) =>
        new(
            deviceRect.Left / dpiScaleX,
            deviceRect.Top / dpiScaleY,
            deviceRect.Width / dpiScaleX,
            deviceRect.Height / dpiScaleY);

    public static Rectangle DisplayRectToPixelRect(Rect displayRect, System.Windows.Size overlaySize, System.Windows.Size snapshotPixelSize)
    {
        if (overlaySize.Width <= 0 ||
            overlaySize.Height <= 0 ||
            snapshotPixelSize.Width <= 0 ||
            snapshotPixelSize.Height <= 0 ||
            displayRect.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var left = (int)Math.Floor(displayRect.Left / overlaySize.Width * snapshotPixelSize.Width);
        var top = (int)Math.Floor(displayRect.Top / overlaySize.Height * snapshotPixelSize.Height);
        var right = (int)Math.Ceiling(displayRect.Right / overlaySize.Width * snapshotPixelSize.Width);
        var bottom = (int)Math.Ceiling(displayRect.Bottom / overlaySize.Height * snapshotPixelSize.Height);
        return ClampPixelRectangle(
            Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom)),
            new Rectangle(0, 0, (int)Math.Round(snapshotPixelSize.Width), (int)Math.Round(snapshotPixelSize.Height)));
    }

    public static Rect PixelRectToDisplayRect(Rectangle pixelRect, System.Windows.Size overlaySize, System.Windows.Size snapshotPixelSize)
    {
        if (overlaySize.Width <= 0 ||
            overlaySize.Height <= 0 ||
            snapshotPixelSize.Width <= 0 ||
            snapshotPixelSize.Height <= 0 ||
            pixelRect.IsEmpty)
        {
            return Rect.Empty;
        }

        return new Rect(
            pixelRect.Left / snapshotPixelSize.Width * overlaySize.Width,
            pixelRect.Top / snapshotPixelSize.Height * overlaySize.Height,
            pixelRect.Width / snapshotPixelSize.Width * overlaySize.Width,
            pixelRect.Height / snapshotPixelSize.Height * overlaySize.Height);
    }

    public static IReadOnlyList<ScreenshotWindowTarget> WindowTargetsFromScreenBounds(
        IEnumerable<Rectangle> screenBounds,
        Rectangle virtualScreenBounds)
    {
        var targets = new List<ScreenshotWindowTarget>();
        var snapshotBounds = new Rectangle(0, 0, Math.Max(1, virtualScreenBounds.Width), Math.Max(1, virtualScreenBounds.Height));
        foreach (var bounds in screenBounds)
        {
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                continue;
            }

            var localBounds = new Rectangle(
                bounds.Left - virtualScreenBounds.Left,
                bounds.Top - virtualScreenBounds.Top,
                bounds.Width,
                bounds.Height);
            var clipped = ClampPixelRectangle(localBounds, snapshotBounds);
            if (clipped.Width > 2 && clipped.Height > 2)
            {
                targets.Add(new ScreenshotWindowTarget(clipped));
            }
        }

        return targets;
    }

    public static ScreenshotWindowTarget? HitTestWindowTarget(
        IReadOnlyList<ScreenshotWindowTarget> targets,
        Point displayPoint,
        System.Windows.Size overlaySize,
        System.Windows.Size snapshotPixelSize)
    {
        if (targets.Count == 0 ||
            overlaySize.Width <= 0 ||
            overlaySize.Height <= 0 ||
            snapshotPixelSize.Width <= 0 ||
            snapshotPixelSize.Height <= 0)
        {
            return null;
        }

        var x = (int)Math.Floor(displayPoint.X / overlaySize.Width * snapshotPixelSize.Width);
        var y = (int)Math.Floor(displayPoint.Y / overlaySize.Height * snapshotPixelSize.Height);
        foreach (var target in targets)
        {
            if (target.PixelBounds.Contains(x, y))
            {
                return target;
            }
        }

        return null;
    }

    public static Rectangle MovePixelSelection(Rectangle origin, System.Drawing.Point pixelDelta, Rectangle snapshotBounds)
    {
        if (origin.IsEmpty || snapshotBounds.IsEmpty)
        {
            return origin;
        }

        var left = ClampInt(origin.Left + pixelDelta.X, snapshotBounds.Left, snapshotBounds.Right - origin.Width);
        var top = ClampInt(origin.Top + pixelDelta.Y, snapshotBounds.Top, snapshotBounds.Bottom - origin.Height);
        return new Rectangle(left, top, origin.Width, origin.Height);
    }

    public static byte[] RenderSelectionPng(Bitmap snapshot, Rectangle selectionPixelRect)
    {
        var selection = ClampPixelRectangle(selectionPixelRect, new Rectangle(0, 0, snapshot.Width, snapshot.Height));
        using var crop = snapshot.Clone(selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var stream = new MemoryStream();
        crop.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static byte[] RenderSelectionPng(BitmapSource snapshot, Rectangle selectionPixelRect, BitmapSource? annotations)
    {
        var selection = ClampPixelRectangle(selectionPixelRect, new Rectangle(0, 0, snapshot.PixelWidth, snapshot.PixelHeight));
        var cropped = new CroppedBitmap(snapshot, new Int32Rect(selection.Left, selection.Top, selection.Width, selection.Height));
        var visual = new DrawingVisual();
        using (var drawingContext = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, selection.Width, selection.Height);
            drawingContext.DrawImage(cropped, bounds);
            if (annotations is not null)
            {
                drawingContext.DrawImage(annotations, bounds);
            }
        }

        var bitmap = new RenderTargetBitmap(selection.Width, selection.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static Rect ImageBoundsForSelection(Rect preferredBounds, System.Windows.Size fallbackSize)
    {
        if (!preferredBounds.IsEmpty &&
            double.IsFinite(preferredBounds.Left) &&
            double.IsFinite(preferredBounds.Top) &&
            preferredBounds.Width > 1 &&
            preferredBounds.Height > 1)
        {
            return preferredBounds;
        }

        return new Rect(
            16,
            16,
            Math.Max(1, fallbackSize.Width),
            Math.Max(1, fallbackSize.Height));
    }

    public static Rect MoveSelectionBounds(Rect origin, Vector delta, Rect screen)
    {
        if (origin.IsEmpty)
        {
            return origin;
        }

        var left = ClampInside(origin.Left + delta.X, screen.Left, screen.Right - origin.Width);
        var top = ClampInside(origin.Top + delta.Y, screen.Top, screen.Bottom - origin.Height);
        return new Rect(left, top, origin.Width, origin.Height);
    }

    public static Rect PlaceToolbar(Rect selection, Rect screen, System.Windows.Size toolbarSize, double gap = 10)
    {
        var below = new Rect(selection.Right - toolbarSize.Width, selection.Bottom + gap, toolbarSize.Width, toolbarSize.Height);
        if (screen.Contains(below))
        {
            return below;
        }

        var above = new Rect(selection.Right - toolbarSize.Width, selection.Top - toolbarSize.Height - gap, toolbarSize.Width, toolbarSize.Height);
        if (screen.Contains(above))
        {
            return above;
        }

        var right = new Rect(selection.Right + gap, selection.Top, toolbarSize.Width, toolbarSize.Height);
        if (screen.Contains(right))
        {
            return right;
        }

        var left = new Rect(selection.Left - toolbarSize.Width - gap, selection.Top, toolbarSize.Width, toolbarSize.Height);
        if (screen.Contains(left))
        {
            return left;
        }

        return new Rect(
            ClampInside(selection.Right - toolbarSize.Width, screen.Left + gap, screen.Right - toolbarSize.Width - gap),
            ClampInside(selection.Bottom + gap, screen.Top + gap, screen.Bottom - toolbarSize.Height - gap),
            toolbarSize.Width,
            toolbarSize.Height);
    }

    private static double ClampInside(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);

    private static int ClampInt(int value, int min, int max) =>
        max < min ? min : Math.Clamp(value, min, max);

    public static Rectangle ClampPixelRectangle(Rectangle rectangle, Rectangle bounds)
    {
        if (rectangle.IsEmpty || bounds.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var left = ClampInt(rectangle.Left, bounds.Left, bounds.Right - 1);
        var top = ClampInt(rectangle.Top, bounds.Top, bounds.Bottom - 1);
        var right = ClampInt(rectangle.Right, left + 1, bounds.Right);
        var bottom = ClampInt(rectangle.Bottom, top + 1, bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

public static class ScreenshotFileIO
{
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }
}

public sealed record ScreenshotEncodedFile(string FilePath, byte[] Bytes);

public static class ScreenshotImageEncoder
{
    private const int MinimumEncodedDimension = 32;

    public static ScreenshotEncodedFile EncodeForSave(
        byte[] pngBytes,
        string requestedPath,
        int jpegQuality,
        long maxBytes)
    {
        var wantsJpeg = IsJpegPath(requestedPath);
        var initialBytes = wantsJpeg
            ? EncodeJpeg(LoadBitmap(pngBytes), jpegQuality)
            : pngBytes;
        if (maxBytes <= 0 || initialBytes.LongLength <= maxBytes)
        {
            return new ScreenshotEncodedFile(requestedPath, initialBytes);
        }

        var bitmap = LoadBitmap(pngBytes);
        var jpegPath = wantsJpeg ? requestedPath : Path.ChangeExtension(requestedPath, ".jpg");
        var bestBytes = initialBytes;
        foreach (var quality in QualitySteps(jpegQuality))
        {
            var candidate = EncodeJpeg(bitmap, quality);
            if (candidate.Length < bestBytes.Length)
            {
                bestBytes = candidate;
            }

            if (candidate.LongLength <= maxBytes)
            {
                return new ScreenshotEncodedFile(jpegPath, candidate);
            }
        }

        var scale = 0.85;
        while (bitmap.PixelWidth * scale >= MinimumEncodedDimension &&
               bitmap.PixelHeight * scale >= MinimumEncodedDimension)
        {
            var scaled = ScaleBitmap(bitmap, scale);
            foreach (var quality in QualitySteps(Math.Min(jpegQuality, 85)))
            {
                var candidate = EncodeJpeg(scaled, quality);
                if (candidate.Length < bestBytes.Length)
                {
                    bestBytes = candidate;
                }

                if (candidate.LongLength <= maxBytes)
                {
                    return new ScreenshotEncodedFile(jpegPath, candidate);
                }
            }

            scale *= 0.85;
        }

        return new ScreenshotEncodedFile(jpegPath, bestBytes);
    }

    private static IEnumerable<int> QualitySteps(int preferredQuality)
    {
        var start = Math.Clamp(preferredQuality, 1, 100);
        for (var quality = start; quality >= 35; quality -= 5)
        {
            yield return quality;
        }

        foreach (var quality in new[] { 30, 25, 20, 15, 10, 5, 1 })
        {
            if (quality < start)
            {
                yield return quality;
            }
        }
    }

    private static bool IsJpegPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] EncodeJpeg(BitmapSource source, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(ToJpegCompatibleBitmap(source)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource ToJpegCompatibleBitmap(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgr24)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgr24;
        converted.EndInit();
        converted.Freeze();
        return converted;
    }

    private static BitmapSource ScaleBitmap(BitmapSource source, double scale)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static BitmapImage LoadBitmap(byte[] pngBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed class ScreenshotSnapshotOverlay : Window
{
    private static int _openOverlay;
    private const double SelectionClickTolerance = 4;
    private readonly Canvas _root = new();
    private readonly SnapshotSelectionAdorner _selectionAdorner = new();
    private readonly ScreenCaptureResult _snapshot;
    private readonly Rectangle _virtualScreenBounds;
    private readonly string _initialColor;
    private readonly double _initialWidth;
    private readonly int _jpegQuality;
    private readonly long _maxSavedBytes;
    private readonly Action<string>? _saveColor;
    private readonly bool _selectOnly;
    private readonly bool _startWithWholeImage;
    private readonly IReadOnlyList<ScreenshotWindowTarget> _windowTargets;
    private readonly Slider _width = new();
    private readonly Grid _selectionSurface = new();
    private readonly InkCanvas _inkCanvas = new();
    private readonly Canvas _shapeCanvas = new();
    private readonly List<WpfButton> _toolbarButtons = [];
    private readonly Stack<AnnotationHistoryItem> _undoStack = new();
    private readonly Stack<AnnotationHistoryItem> _redoStack = new();
    private BitmapImage? _snapshotBitmap;
    private Border? _toolbarContainer;
    private Border? _selectionHitBox;
    private Border? _magnifier;
    private System.Windows.Controls.Image? _magnifierImage;
    private TextBlock? _magnifierText;
    private Viewbox? _annotationView;
    private Border? _colorSwatch;
    private TextBlock? _widthValueText;
    private System.Windows.Controls.Primitives.Popup? _colorPopup;
    private TextBlock? _statusText;
    private Point? _selectStart;
    private ScreenshotWindowTarget? _hoverWindowTarget;
    private ScreenshotWindowTarget? _clickWindowTarget;
    private Point? _moveStart;
    private Rectangle _moveOriginPixel;
    private System.Windows.Shapes.Shape? _activeShape;
    private Point? _shapeStart;
    private Rectangle _selectionPixelRect;
    private Rect _selectionDisplayRect;
    private ScreenCaptureResult? _captureResult;
    private string _currentTool = "Move";
    private string _currentColor;
    private bool _editing;
    private bool _saving;

    private sealed record AnnotationHistoryItem(System.Windows.Ink.Stroke? Stroke, UIElement? Element);

    private ScreenshotSnapshotOverlay(
        ScreenCaptureResult snapshot,
        Rectangle virtualScreenBounds,
        string initialColor,
        double initialWidth,
        int jpegQuality,
        long maxSavedBytes,
        Action<string>? saveColor,
        bool selectOnly,
        bool startWithWholeImage,
        IReadOnlyList<ScreenshotWindowTarget>? windowTargets = null)
    {
        _snapshot = snapshot;
        _virtualScreenBounds = virtualScreenBounds;
        _initialColor = initialColor;
        _initialWidth = initialWidth;
        _jpegQuality = jpegQuality;
        _maxSavedBytes = maxSavedBytes;
        _saveColor = saveColor;
        _selectOnly = selectOnly;
        _startWithWholeImage = startWithWholeImage;
        _windowTargets = windowTargets ?? [];
        _currentColor = NormalizeColorName(initialColor);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = System.Windows.Input.Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _root;

        Loaded += (_, _) => BuildSnapshotSurface();
        MouseDown += OnSelectMouseDown;
        MouseMove += OnSelectMouseMove;
        MouseUp += OnSelectMouseUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public static async ValueTask<ScreenCaptureResult?> CaptureRegionAndEditAsync(
        ScreenCaptureResult snapshot,
        Rectangle virtualScreenBounds,
        IReadOnlyList<ScreenshotWindowTarget> windowTargets,
        string initialColor,
        double initialWidth,
        int jpegQuality,
        long maxSavedBytes,
        Action<string>? saveColor,
        CancellationToken cancellationToken)
    {
        return await ShowOverlayAsync(
            new ScreenshotSnapshotOverlay(snapshot, virtualScreenBounds, initialColor, initialWidth, jpegQuality, maxSavedBytes, saveColor, selectOnly: false, startWithWholeImage: false, windowTargets: windowTargets),
            overlay => overlay._captureResult,
            cancellationToken);
    }

    public static async ValueTask<Rectangle?> SelectRegionAsync(
        ScreenCaptureResult snapshot,
        Rectangle virtualScreenBounds,
        IReadOnlyList<ScreenshotWindowTarget> windowTargets,
        CancellationToken cancellationToken)
    {
        return await ShowOverlayAsync<Rectangle?>(
            new ScreenshotSnapshotOverlay(snapshot, virtualScreenBounds, "Red", 4, 90, 0, null, selectOnly: true, startWithWholeImage: false, windowTargets: windowTargets),
            overlay => overlay._selectionPixelRect.IsEmpty ? null : overlay._selectionPixelRect,
            cancellationToken);
    }

    public static async ValueTask<ScreenCaptureResult?> EditImageAsync(
        ScreenCaptureResult capture,
        string initialColor,
        double initialWidth,
        int jpegQuality,
        long maxSavedBytes,
        Action<string>? saveColor,
        CancellationToken cancellationToken)
    {
        return await ShowOverlayAsync(
            new ScreenshotSnapshotOverlay(capture, new Rectangle(0, 0, capture.Width, capture.Height), initialColor, initialWidth, jpegQuality, maxSavedBytes, saveColor, selectOnly: false, startWithWholeImage: true),
            overlay => overlay._captureResult,
            cancellationToken);
    }

    private static async ValueTask<TResult> ShowOverlayAsync<TResult>(
        ScreenshotSnapshotOverlay overlay,
        Func<ScreenshotSnapshotOverlay, TResult> result,
        CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return default!;
        }

        return await dispatcher.InvokeAsync(() =>
        {
            if (!TryEnterOverlay())
            {
                return default!;
            }

            try
            {
                cancellationToken.Register(() => overlay.Dispatcher.Invoke(() => overlay.Close()));
                overlay.ShowDialog();
                return result(overlay);
            }
            finally
            {
                ExitOverlay();
            }
        }).Task.WaitAsync(cancellationToken);
    }

    private static bool TryEnterOverlay() => Interlocked.CompareExchange(ref _openOverlay, 1, 0) == 0;

    private static void ExitOverlay() => Interlocked.Exchange(ref _openOverlay, 0);

    private void BuildSnapshotSurface()
    {
        _snapshotBitmap = LoadBitmap(_snapshot);
        _root.Children.Clear();
        _root.Children.Add(new System.Windows.Controls.Image
        {
            Source = _snapshotBitmap,
            Width = ActualWidth,
            Height = ActualHeight,
            Stretch = Stretch.Fill
        });

        _selectionAdorner.Width = ActualWidth;
        _selectionAdorner.Height = ActualHeight;
        _selectionAdorner.IsHitTestVisible = false;
        _root.Children.Add(_selectionAdorner);
        BuildMagnifier();

        if (_startWithWholeImage)
        {
            EnterEdit(new Rectangle(0, 0, _snapshotBitmap.PixelWidth, _snapshotBitmap.PixelHeight));
        }
    }

    private void OnSelectMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _selectStart = e.GetPosition(this);
        _clickWindowTarget = HitTestWindowTarget(_selectStart.Value);
        UpdateMagnifier(_selectStart.Value);
        CaptureMouse();
    }

    private void OnSelectMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_editing)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (_selectStart is null)
        {
            UpdateWindowHover(current);
            return;
        }

        _selectionDisplayRect = ToRect(_selectStart.Value, current);
        if (IsClickSized(_selectionDisplayRect) && _clickWindowTarget is not null)
        {
            ShowWindowTarget(_clickWindowTarget);
            UpdateMagnifier(current);
            return;
        }

        _clickWindowTarget = null;
        _selectionAdorner.Selection = _selectionDisplayRect;
        UpdateMagnifier(current);
    }

    private void OnSelectMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_editing || _selectStart is null || _snapshotBitmap is null)
        {
            return;
        }

        ReleaseMouseCapture();
        HideMagnifier();
        var displayRect = ToRect(_selectStart.Value, e.GetPosition(this));
        _selectStart = null;
        if (IsClickSized(displayRect))
        {
            var target = _clickWindowTarget ?? HitTestWindowTarget(e.GetPosition(this));
            _clickWindowTarget = null;
            if (target is not null)
            {
                AcceptSelection(target.PixelBounds);
                return;
            }

            Close();
            return;
        }

        _clickWindowTarget = null;
        var pixelRect = ScreenshotOverlayLayout.DisplayRectToPixelRect(
            displayRect,
            OverlaySize(),
            SnapshotPixelSize());
        if (pixelRect.Width <= 2 || pixelRect.Height <= 2)
        {
            Close();
            return;
        }

        AcceptSelection(pixelRect);
    }

    private bool IsClickSized(Rect rect) =>
        rect.Width <= SelectionClickTolerance && rect.Height <= SelectionClickTolerance;

    private void UpdateWindowHover(Point displayPoint)
    {
        var target = HitTestWindowTarget(displayPoint);
        if (Equals(target, _hoverWindowTarget))
        {
            return;
        }

        _hoverWindowTarget = target;
        if (target is null)
        {
            Cursor = System.Windows.Input.Cursors.Cross;
            _selectionAdorner.Selection = Rect.Empty;
            return;
        }

        Cursor = System.Windows.Input.Cursors.Cross;
        ShowWindowTarget(target);
    }

    private void ShowWindowTarget(ScreenshotWindowTarget target)
    {
        _selectionAdorner.Selection = ScreenshotOverlayLayout.PixelRectToDisplayRect(
            target.PixelBounds,
            OverlaySize(),
            SnapshotPixelSize());
    }

    private ScreenshotWindowTarget? HitTestWindowTarget(Point displayPoint) =>
        ScreenshotOverlayLayout.HitTestWindowTarget(
            _windowTargets,
            displayPoint,
            OverlaySize(),
            SnapshotPixelSize());

    private void AcceptSelection(Rectangle pixelRect)
    {
        if (_selectOnly)
        {
            _selectionPixelRect = pixelRect;
            Close();
            return;
        }

        EnterEdit(pixelRect);
    }

    private void BuildMagnifier()
    {
        _magnifierImage = new System.Windows.Controls.Image
        {
            Width = 96,
            Height = 96,
            Stretch = Stretch.Fill
        };
        _magnifierText = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var panel = new StackPanel();
        panel.Children.Add(_magnifierImage);
        panel.Children.Add(_magnifierText);
        _magnifier = new Border
        {
            Width = 112,
            Height = 132,
            Padding = new Thickness(7),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 20, 23, 31)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 102, 122)),
            BorderThickness = new Thickness(1),
            Child = panel,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _root.Children.Add(_magnifier);
    }

    private void UpdateMagnifier(Point displayPoint)
    {
        if (_magnifier is null ||
            _magnifierImage is null ||
            _magnifierText is null ||
            _snapshotBitmap is null ||
            _editing)
        {
            return;
        }

        var pixel = ScreenshotOverlayLayout.DisplayRectToPixelRect(
            new Rect(displayPoint.X, displayPoint.Y, 1, 1),
            OverlaySize(),
            SnapshotPixelSize());
        if (pixel.IsEmpty)
        {
            return;
        }

        const int sampleSize = 42;
        var sample = ScreenshotOverlayLayout.ClampPixelRectangle(
            new Rectangle(pixel.Left - sampleSize / 2, pixel.Top - sampleSize / 2, sampleSize, sampleSize),
            new Rectangle(0, 0, _snapshotBitmap.PixelWidth, _snapshotBitmap.PixelHeight));
        if (sample.Width <= 1 || sample.Height <= 1)
        {
            return;
        }

        _magnifierImage.Source = new CroppedBitmap(
            _snapshotBitmap,
            new Int32Rect(sample.Left, sample.Top, sample.Width, sample.Height));
        _magnifierText.Text = $"{pixel.Left}, {pixel.Top}";
        PositionMagnifier(displayPoint);
        _magnifier.Visibility = Visibility.Visible;
    }

    private void PositionMagnifier(Point displayPoint)
    {
        if (_magnifier is null)
        {
            return;
        }

        const double gap = 18;
        var left = displayPoint.X + gap;
        var top = displayPoint.Y + gap;
        if (left + _magnifier.Width > ActualWidth - gap)
        {
            left = displayPoint.X - _magnifier.Width - gap;
        }

        if (top + _magnifier.Height > ActualHeight - gap)
        {
            top = displayPoint.Y - _magnifier.Height - gap;
        }

        Canvas.SetLeft(_magnifier, Math.Clamp(left, gap, Math.Max(gap, ActualWidth - _magnifier.Width - gap)));
        Canvas.SetTop(_magnifier, Math.Clamp(top, gap, Math.Max(gap, ActualHeight - _magnifier.Height - gap)));
    }

    private void HideMagnifier()
    {
        if (_magnifier is not null)
        {
            _magnifier.Visibility = Visibility.Collapsed;
        }
    }

    private void EnterEdit(Rectangle pixelRect)
    {
        if (_snapshotBitmap is null)
        {
            return;
        }

        _editing = true;
        Cursor = System.Windows.Input.Cursors.SizeAll;
        _selectionPixelRect = pixelRect;
        _selectionDisplayRect = ScreenshotOverlayLayout.PixelRectToDisplayRect(pixelRect, OverlaySize(), SnapshotPixelSize());
        _selectionAdorner.Selection = _selectionDisplayRect;

        _selectionSurface.Width = pixelRect.Width;
        _selectionSurface.Height = pixelRect.Height;
        _selectionSurface.Background = System.Windows.Media.Brushes.Transparent;
        _selectionSurface.Children.Clear();

        _inkCanvas.Width = pixelRect.Width;
        _inkCanvas.Height = pixelRect.Height;
        _inkCanvas.Background = System.Windows.Media.Brushes.Transparent;
        _inkCanvas.Strokes.Clear();
        _inkCanvas.StrokeCollected -= InkCanvas_StrokeCollected;
        _inkCanvas.StrokeCollected += InkCanvas_StrokeCollected;
        _selectionSurface.Children.Add(_inkCanvas);

        _shapeCanvas.Width = pixelRect.Width;
        _shapeCanvas.Height = pixelRect.Height;
        _shapeCanvas.Background = System.Windows.Media.Brushes.Transparent;
        _shapeCanvas.Children.Clear();
        _shapeCanvas.MouseDown -= ShapeCanvas_MouseDown;
        _shapeCanvas.MouseMove -= ShapeCanvas_MouseMove;
        _shapeCanvas.MouseUp -= ShapeCanvas_MouseUp;
        _shapeCanvas.MouseDown += ShapeCanvas_MouseDown;
        _shapeCanvas.MouseMove += ShapeCanvas_MouseMove;
        _shapeCanvas.MouseUp += ShapeCanvas_MouseUp;
        _selectionSurface.Children.Add(_shapeCanvas);
        _undoStack.Clear();
        _redoStack.Clear();

        _annotationView = new Viewbox
        {
            Stretch = Stretch.Fill,
            Child = _selectionSurface
        };
        _root.Children.Add(_annotationView);

        _selectionHitBox = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll
        };
        _selectionHitBox.MouseLeftButtonDown += SelectionHitBox_MouseLeftButtonDown;
        _selectionHitBox.MouseMove += SelectionHitBox_MouseMove;
        _selectionHitBox.MouseLeftButtonUp += SelectionHitBox_MouseLeftButtonUp;
        _root.Children.Add(_selectionHitBox);

        var toolbar = BuildToolbar();
        _root.Children.Add(toolbar);
        ConfigureTooling();
        SetSelectionDisplayRect(_selectionDisplayRect);
    }

    private Border BuildToolbar()
    {
        var toolbar = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(6)
        };
        _toolbarButtons.Clear();

        var tools = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        tools.Children.Add(ToolButton("Move", "M12,3 V21 M12,3 L8,7 M12,3 L16,7 M12,21 L8,17 M12,21 L16,17 M3,12 H21 M3,12 L7,8 M3,12 L7,16 M21,12 L17,8 M21,12 L17,16", "Move"));
        tools.Children.Add(ToolButton("Pen", "M3,19 L14,8 L18,12 L7,23 L2,24 Z", "Pen"));
        tools.Children.Add(ToolButton("Rectangle", "M4,5 H22 V19 H4 Z", "Rectangle"));
        tools.Children.Add(ToolButton("Ellipse", "M13,5 C18,5 22,8 22,13 C22,18 18,21 13,21 C8,21 4,18 4,13 C4,8 8,5 13,5 Z", "Ellipse"));
        tools.Children.Add(Separator());
        tools.Children.Add(ColorPickerButton());

        _width.Minimum = 2;
        _width.Maximum = 18;
        _width.Value = Math.Clamp(_initialWidth, 2, 18);
        tools.Children.Add(LineWidthControl());
        tools.Children.Add(Separator());
        tools.Children.Add(IconButton("M8,7 H4 V3 M5,7 C7,4 11,4 14,6 C17,8 18,12 16,15 C14,19 9,20 5,17", "Undo", (_, _) => Undo()));
        tools.Children.Add(IconButton("M16,7 H20 V3 M19,7 C17,4 13,4 10,6 C7,8 6,12 8,15 C10,19 15,20 19,17", "Redo", (_, _) => Redo()));
        tools.Children.Add(IconButton("M5,7 H19 M9,7 V5 H15 V7 M8,10 V20 M12,10 V20 M16,10 V20 M6,7 L7,22 H17 L18,7", "Clear annotations", (_, _) => ClearAnnotations()));
        toolbar.Children.Add(tools);

        _statusText = new TextBlock
        {
            Text = string.Empty,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120,
            Margin = new Thickness(6, 0, 4, 0)
        };
        toolbar.Children.Add(_statusText);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        actions.Children.Add(IconButton("M5,5 H19 V21 H5 Z M8,5 V12 H16 V5 M8,18 H16", "Save", (_, _) => SaveAndClose(copy: false)));
        actions.Children.Add(IconButton("M7,7 L19,19 M19,7 L7,19", "Cancel", (_, _) => Close()));
        actions.Children.Add(IconButton("M5,13 L10,18 L20,7", "Copy to clipboard", (_, _) => SaveAndClose(copy: true)));
        toolbar.Children.Add(actions);

        _toolbarContainer = new Border
        {
            Child = toolbar,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 24, 27, 34)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 85, 103)),
            BorderThickness = new Thickness(1)
        };
        return _toolbarContainer;
    }

    private void ConfigureTooling()
    {
        _width.ValueChanged -= Tooling_Changed;
        _width.ValueChanged += Tooling_Changed;
        ApplyEditingMode();
        ApplyInkAttributes();
        ApplyToolState();
    }

    private void Tooling_Changed(object sender, RoutedEventArgs e)
    {
        ApplyInkAttributes();
        if (_widthValueText is not null)
        {
            _widthValueText.Text = $"{Math.Round(_width.Value)} px";
        }
    }

    private void ApplyEditingMode()
    {
        var tool = CurrentTool();
        _inkCanvas.EditingMode = tool == "Pen" ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        _inkCanvas.IsHitTestVisible = tool == "Pen";
        _shapeCanvas.IsHitTestVisible = tool is "Rectangle" or "Ellipse";
        if (_annotationView is not null)
        {
            _annotationView.IsHitTestVisible = tool is "Pen" or "Rectangle" or "Ellipse";
        }

        if (_selectionHitBox is not null)
        {
            _selectionHitBox.IsHitTestVisible = tool == "Move";
        }

        Cursor = tool == "Move" ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Cross;
    }

    private void SelectionHitBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || CurrentTool() != "Move")
        {
            return;
        }

        _moveStart = e.GetPosition(this);
        _moveOriginPixel = _selectionPixelRect;
        _selectionHitBox?.CaptureMouse();
        e.Handled = true;
    }

    private void SelectionHitBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_moveStart is null || e.LeftButton != MouseButtonState.Pressed || _snapshotBitmap is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        var pixelDelta = DisplayDeltaToPixelDelta(current - _moveStart.Value);
        _selectionPixelRect = ScreenshotOverlayLayout.MovePixelSelection(
            _moveOriginPixel,
            pixelDelta,
            new Rectangle(0, 0, _snapshotBitmap.PixelWidth, _snapshotBitmap.PixelHeight));
        _selectionDisplayRect = ScreenshotOverlayLayout.PixelRectToDisplayRect(_selectionPixelRect, OverlaySize(), SnapshotPixelSize());
        SetSelectionDisplayRect(_selectionDisplayRect);
        e.Handled = true;
    }

    private void SelectionHitBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_moveStart is null)
        {
            return;
        }

        _selectionHitBox?.ReleaseMouseCapture();
        _moveStart = null;
        e.Handled = true;
    }

    private void SetSelectionDisplayRect(Rect bounds)
    {
        _selectionDisplayRect = bounds;
        _selectionAdorner.Selection = bounds;
        if (_annotationView is not null)
        {
            _annotationView.Width = bounds.Width;
            _annotationView.Height = bounds.Height;
            Canvas.SetLeft(_annotationView, bounds.Left);
            Canvas.SetTop(_annotationView, bounds.Top);
        }

        if (_selectionHitBox is not null)
        {
            _selectionHitBox.Width = bounds.Width;
            _selectionHitBox.Height = bounds.Height;
            Canvas.SetLeft(_selectionHitBox, bounds.Left);
            Canvas.SetTop(_selectionHitBox, bounds.Top);
        }

        UpdateToolbarPlacement();
    }

    private void UpdateToolbarPlacement()
    {
        if (_toolbarContainer is null || _selectionDisplayRect.IsEmpty)
        {
            return;
        }

        _toolbarContainer.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = _toolbarContainer.DesiredSize.Width > 0
            ? _toolbarContainer.DesiredSize
            : new System.Windows.Size(380, 44);
        var toolbarBounds = ScreenshotOverlayLayout.PlaceToolbar(
            _selectionDisplayRect,
            new Rect(0, 0, ActualWidth, ActualHeight),
            toolbarSize);
        Canvas.SetLeft(_toolbarContainer, toolbarBounds.X);
        Canvas.SetTop(_toolbarContainer, toolbarBounds.Y);
    }

    private WpfButton ToolButton(string tool, string icon, string tooltip)
    {
        var button = IconButton(icon, tooltip, (_, _) =>
        {
            _currentTool = tool;
            ApplyEditingMode();
            ApplyToolState();
        });
        button.Tag = tool;
        return button;
    }

    private WpfButton ColorPickerButton()
    {
        _colorSwatch = new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(8),
            Background = BrushForName(_currentColor),
            BorderBrush = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(_currentColor == "White" ? 1 : 0)
        };
        var button = new WpfButton
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = "Color",
            Content = _colorSwatch,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => ShowColorPopup(button);
        button.Tag = "color";
        _toolbarButtons.Add(button);
        return button;
    }

    private Border LineWidthControl()
    {
        _width.Width = 70;
        _width.Height = 28;
        _width.Margin = new Thickness(8, 0, 8, 0);
        _width.VerticalAlignment = VerticalAlignment.Center;

        _widthValueText = new TextBlock
        {
            Text = $"{Math.Round(_width.Value)} px",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            MinWidth = 28,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(_width);
        row.Children.Add(_widthValueText);

        return new Border
        {
            Height = 30,
            Margin = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(58, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private void ShowColorPopup(WpfButton button)
    {
        if (_colorPopup?.IsOpen == true)
        {
            _colorPopup.IsOpen = false;
            return;
        }

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(7)
        };
        foreach (var color in new[] { "Red", "Yellow", "Green", "Blue", "White" })
        {
            row.Children.Add(ColorChoiceButton(color));
        }

        _colorPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 27, 34)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 85, 103)),
                BorderThickness = new Thickness(1),
                Child = row
            }
        };
        _colorPopup.IsOpen = true;
    }

    private WpfButton ColorChoiceButton(string color)
    {
        var swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = BrushForName(color),
            BorderBrush = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(color == "White" || color == _currentColor ? 1 : 0)
        };
        var button = new WpfButton
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = color,
            Content = swatch,
            Background = color == _currentColor
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 61, 53))
                : System.Windows.Media.Brushes.Transparent,
            BorderBrush = color == _currentColor
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 210, 151))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(58, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => SelectColor(color);
        return button;
    }

    private void SelectColor(string color)
    {
        _currentColor = color;
        if (_colorSwatch is not null)
        {
            _colorSwatch.Background = BrushForName(color);
            _colorSwatch.BorderThickness = new Thickness(color == "White" ? 1 : 0);
        }

        _saveColor?.Invoke(color);
        if (_colorPopup is not null)
        {
            _colorPopup.IsOpen = false;
        }

        ApplyInkAttributes();
        ApplyToolState();
    }

    private WpfButton IconButton(string geometry, string tooltip, RoutedEventHandler onClick)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
        var button = new WpfButton
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(4),
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = tooltip,
            Content = path,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255))
        };
        button.Click += onClick;
        _toolbarButtons.Add(button);
        return button;
    }

    private static UIElement Separator() => new Border
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(7, 0, 4, 0),
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(74, 255, 255, 255))
    };

    private void ApplyToolState()
    {
        foreach (var button in _toolbarButtons)
        {
            var active = button.Tag?.ToString() == _currentTool ||
                         button.Tag?.ToString() == "color";
            button.BorderBrush = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 210, 151))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255));
        }
    }

    private void ShapeCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            CurrentTool() is not ("Rectangle" or "Ellipse"))
        {
            return;
        }

        _shapeStart = e.GetPosition(_shapeCanvas);
        _activeShape = CurrentTool() == "Rectangle"
            ? new System.Windows.Shapes.Rectangle()
            : new System.Windows.Shapes.Ellipse();
        _activeShape.Stroke = CurrentBrush();
        _activeShape.StrokeThickness = _width.Value;
        _activeShape.Fill = System.Windows.Media.Brushes.Transparent;
        Canvas.SetLeft(_activeShape, _shapeStart.Value.X);
        Canvas.SetTop(_activeShape, _shapeStart.Value.Y);
        _shapeCanvas.Children.Add(_activeShape);
        _shapeCanvas.CaptureMouse();
    }

    private void InkCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        _undoStack.Push(new AnnotationHistoryItem(e.Stroke, null));
        _redoStack.Clear();
    }

    private void ShapeCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_shapeStart is null || _activeShape is null)
        {
            return;
        }

        var current = e.GetPosition(_shapeCanvas);
        var left = Math.Min(_shapeStart.Value.X, current.X);
        var top = Math.Min(_shapeStart.Value.Y, current.Y);
        var width = Math.Abs(current.X - _shapeStart.Value.X);
        var height = Math.Abs(current.Y - _shapeStart.Value.Y);
        Canvas.SetLeft(_activeShape, left);
        Canvas.SetTop(_activeShape, top);
        _activeShape.Width = width;
        _activeShape.Height = height;
    }

    private void ShapeCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _shapeCanvas.ReleaseMouseCapture();
        if (_activeShape is not null && (_activeShape.Width < 3 || _activeShape.Height < 3))
        {
            _shapeCanvas.Children.Remove(_activeShape);
        }
        else if (_activeShape is not null)
        {
            _undoStack.Push(new AnnotationHistoryItem(null, _activeShape));
            _redoStack.Clear();
        }

        _activeShape = null;
        _shapeStart = null;
    }

    private async void SaveAndClose(bool copy)
    {
        if (_saving || _snapshotBitmap is null || _selectionPixelRect.IsEmpty)
        {
            return;
        }

        try
        {
            _saving = true;
            string? outputPath = null;
            if (!copy && !TryChooseSavePath(_snapshot.FilePath, out outputPath))
            {
                _saving = false;
                SetToolbarEnabled(true, string.Empty);
                return;
            }

            SetToolbarEnabled(false, copy ? "Copying..." : "Saving...");
            var bytes = ScreenshotOverlayLayout.RenderSelectionPng(
                _snapshotBitmap,
                _selectionPixelRect,
                RenderAnnotationsBitmap());
            if (copy)
            {
                var clipboardImage = LoadBitmap(bytes);
                await SetClipboardImageAsync(clipboardImage);
                _captureResult = new ScreenCaptureResult
                {
                    FilePath = string.Empty,
                    ImagePng = bytes,
                    Width = _selectionPixelRect.Width,
                    Height = _selectionPixelRect.Height,
                    CopiedToClipboard = true
                };
            }
            else
            {
                var output = ScreenshotImageEncoder.EncodeForSave(bytes, outputPath!, _jpegQuality, _maxSavedBytes);
                await Task.Run(() => ScreenshotFileIO.WriteAllBytesAtomic(output.FilePath, output.Bytes));
                _captureResult = new ScreenCaptureResult
                {
                    FilePath = output.FilePath,
                    Width = _selectionPixelRect.Width,
                    Height = _selectionPixelRect.Height,
                    CopiedToClipboard = false
                };
            }

            Close();
        }
        catch (Exception ex)
        {
            _saving = false;
            SetToolbarEnabled(true, $"Failed: {ex.Message}");
        }
    }

    private BitmapSource? RenderAnnotationsBitmap()
    {
        if (_selectionPixelRect.IsEmpty)
        {
            return null;
        }

        _selectionSurface.Measure(new System.Windows.Size(_selectionPixelRect.Width, _selectionPixelRect.Height));
        _selectionSurface.Arrange(new Rect(0, 0, _selectionPixelRect.Width, _selectionPixelRect.Height));
        _selectionSurface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            _selectionPixelRect.Width,
            _selectionPixelRect.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(_selectionSurface);
        bitmap.Freeze();
        return bitmap;
    }

    private bool TryChooseSavePath(string currentPath, out string? outputPath)
    {
        outputPath = null;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            DefaultExt = Path.GetExtension(currentPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(currentPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(currentPath)
                ? $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}{(Path.GetExtension(currentPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png")}"
                : Path.GetFileName(currentPath)
        };
        if (dialog.DefaultExt == ".jpg")
        {
            dialog.FilterIndex = 2;
        }

        var directory = string.IsNullOrWhiteSpace(currentPath) ? null : Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        var wasTopmost = Topmost;
        Topmost = false;
        try
        {
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            outputPath = dialog.FileName;
            return !string.IsNullOrWhiteSpace(outputPath);
        }
        finally
        {
            Topmost = wasTopmost;
            Activate();
        }
    }

    private void SetToolbarEnabled(bool enabled, string status)
    {
        foreach (var button in _toolbarButtons)
        {
            button.IsEnabled = enabled;
        }

        _width.IsEnabled = enabled;
        if (_statusText is not null)
        {
            _statusText.Text = status;
        }
    }

    private void Undo()
    {
        if (!_undoStack.TryPop(out var item))
        {
            return;
        }

        RemoveAnnotationItem(item);
        _redoStack.Push(item);
    }

    private void Redo()
    {
        if (!_redoStack.TryPop(out var item))
        {
            return;
        }

        AddAnnotationItem(item);
        _undoStack.Push(item);
    }

    private void ClearAnnotations()
    {
        _inkCanvas.Strokes.Clear();
        _shapeCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void RemoveAnnotationItem(AnnotationHistoryItem item)
    {
        if (item.Stroke is not null)
        {
            _inkCanvas.Strokes.Remove(item.Stroke);
        }
        else if (item.Element is not null)
        {
            _shapeCanvas.Children.Remove(item.Element);
        }
    }

    private void AddAnnotationItem(AnnotationHistoryItem item)
    {
        if (item.Stroke is not null && !_inkCanvas.Strokes.Contains(item.Stroke))
        {
            _inkCanvas.Strokes.Add(item.Stroke);
        }
        else if (item.Element is not null && !_shapeCanvas.Children.Contains(item.Element))
        {
            _shapeCanvas.Children.Add(item.Element);
        }
    }

    private void ApplyInkAttributes()
    {
        _inkCanvas.DefaultDrawingAttributes.Color = ((SolidColorBrush)CurrentBrush()).Color;
        _inkCanvas.DefaultDrawingAttributes.Width = _width.Value;
        _inkCanvas.DefaultDrawingAttributes.Height = _width.Value;
    }

    private System.Drawing.Point DisplayDeltaToPixelDelta(Vector delta)
    {
        var overlaySize = OverlaySize();
        var pixelSize = SnapshotPixelSize();
        if (overlaySize.Width <= 0 || overlaySize.Height <= 0)
        {
            return System.Drawing.Point.Empty;
        }

        return new System.Drawing.Point(
            (int)Math.Round(delta.X / overlaySize.Width * pixelSize.Width),
            (int)Math.Round(delta.Y / overlaySize.Height * pixelSize.Height));
    }

    private System.Windows.Size OverlaySize() => new(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));

    private System.Windows.Size SnapshotPixelSize() =>
        _snapshotBitmap is null
            ? new System.Windows.Size(Math.Max(1, _snapshot.Width), Math.Max(1, _snapshot.Height))
            : new System.Windows.Size(_snapshotBitmap.PixelWidth, _snapshotBitmap.PixelHeight);

    private string CurrentTool() => _currentTool;

    private System.Windows.Media.Brush CurrentBrush() => BrushForName(_currentColor);

    private static System.Windows.Media.Brush BrushForName(string color) => color switch
    {
        "Yellow" => System.Windows.Media.Brushes.Yellow,
        "Green" => System.Windows.Media.Brushes.LimeGreen,
        "Blue" => System.Windows.Media.Brushes.DeepSkyBlue,
        "White" => System.Windows.Media.Brushes.White,
        _ => System.Windows.Media.Brushes.Red
    };

    private static Rect ToRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static string NormalizeColorName(string value)
    {
        var allowed = new[] { "Red", "Yellow", "Green", "Blue", "White" };
        return allowed.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "Red";
    }

    private static BitmapImage LoadBitmap(ScreenCaptureResult capture)
    {
        if (capture.ImagePng is { Length: > 0 } bytes)
        {
            return LoadBitmap(bytes);
        }

        return LoadBitmap(capture.FilePath);
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage LoadBitmap(byte[] pngBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task SetClipboardImageAsync(BitmapSource image)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return;
            }
            catch (COMException ex)
            {
                lastError = ex;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }

            await Task.Delay(60);
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }
}

internal sealed class SnapshotSelectionAdorner : FrameworkElement
{
    private Rect _selection;

    public Rect Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        if (Selection.Width <= 0 || Selection.Height <= 0)
        {
            drawingContext.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(54, 0, 0, 0)), null, full);
            return;
        }

        var dim = new SolidColorBrush(System.Windows.Media.Color.FromArgb(112, 0, 0, 0));
        var fullGeometry = new RectangleGeometry(full);
        var selectionGeometry = new RectangleGeometry(Selection);
        drawingContext.DrawGeometry(dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, fullGeometry, selectionGeometry));
        drawingContext.DrawRectangle(null, new System.Windows.Media.Pen(ScreenshotSelectionStyle.BorderBrush, ScreenshotSelectionStyle.BorderThickness), Selection);
        var text = new FormattedText(
            $"{Math.Round(Selection.Width)} x {Math.Round(Selection.Height)}",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            System.Windows.Media.Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(Selection.Right + 8, Selection.Bottom + 8));
    }
}

internal sealed class ScreenshotCaptureOverlay : Window
{
    private static int _openOverlay;
    private readonly Canvas _root = new();
    private readonly CanvasGeometry _selectionGeometry = new();
    private readonly Func<Rectangle, ScreenCaptureResult>? _capture;
    private readonly Action<string>? _saveColor;
    private readonly string _initialColor;
    private readonly double _initialWidth;
    private readonly Rect _virtualBounds;
    private readonly Slider _width = new();
    private readonly Grid _surface = new();
    private readonly InkCanvas _inkCanvas = new();
    private readonly Canvas _shapeCanvas = new();
    private readonly List<WpfButton> _toolbarButtons = [];
    private System.Windows.Controls.Image? _baseImage;
    private Border? _toolbarContainer;
    private Border? _imageHolder;
    private Border? _colorSwatch;
    private System.Windows.Shapes.Rectangle? _imageFrame;
    private TextBlock? _statusText;
    private Point? _start;
    private Point? _moveStart;
    private Point? _moveOrigin;
    private ScreenCaptureResult? _captureResult;
    private ScreenCaptureResult? _editingCapture;
    private Rectangle _currentDeviceBounds;
    private System.Windows.Shapes.Shape? _activeShape;
    private Point? _shapeStart;
    private Rect _imageBounds;
    private string _currentTool = "Move";
    private string _currentColor;
    private bool _editing;
    private bool _saving;

    private ScreenshotCaptureOverlay(
        Func<Rectangle, ScreenCaptureResult>? capture,
        string initialColor,
        double initialWidth,
        Action<string>? saveColor)
    {
        _capture = capture;
        _saveColor = saveColor;
        _initialColor = initialColor;
        _initialWidth = initialWidth;
        _currentColor = NormalizeColorName(initialColor);
        _virtualBounds = GetVirtualScreenBoundsDip();

        WindowStyle = WindowStyle.None;
        // A resizable borderless WPF window still has an invisible resize frame,
        // which shifts PointToScreen/PointFromScreen and breaks screenshot alignment.
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = System.Windows.Input.Cursors.Cross;
        Left = _virtualBounds.Left;
        Top = _virtualBounds.Top;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = _root;

        _root.Children.Add(_selectionGeometry);
        _selectionGeometry.Width = _virtualBounds.Width;
        _selectionGeometry.Height = _virtualBounds.Height;

        MouseDown += OnSelectMouseDown;
        MouseMove += OnSelectMouseMove;
        MouseUp += OnSelectMouseUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public static async ValueTask<ScreenCaptureResult?> CaptureRegionAndEditAsync(
        Func<Rectangle, ScreenCaptureResult> capture,
        string initialColor,
        double initialWidth,
        Action<string>? saveColor,
        CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return null;
        }

        return await dispatcher.InvokeAsync(() =>
        {
            if (!TryEnterOverlay())
            {
                return null;
            }

            var overlay = new ScreenshotCaptureOverlay(capture, initialColor, initialWidth, saveColor);
            try
            {
                cancellationToken.Register(() => overlay.Dispatcher.Invoke(() =>
                {
                    overlay.Close();
                }));
                overlay.ShowDialog();
                return overlay._captureResult;
            }
            finally
            {
                ExitOverlay();
            }
        }).Task.WaitAsync(cancellationToken);
    }

    public static async ValueTask<ScreenCaptureResult?> EditExistingAsync(
        ScreenCaptureResult capture,
        Rectangle preferredBounds,
        string initialColor,
        double initialWidth,
        Action<string>? saveColor,
        CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return capture;
        }

        return await dispatcher.InvokeAsync(() =>
        {
            if (!TryEnterOverlay())
            {
                return null;
            }

            var overlay = new ScreenshotCaptureOverlay(null, initialColor, initialWidth, saveColor);
            try
            {
                cancellationToken.Register(() => overlay.Dispatcher.Invoke(() =>
                {
                    overlay.Close();
                }));
                overlay.Loaded += (_, _) => overlay.BeginEdit(capture, preferredBounds);
                overlay.ShowDialog();
                return overlay._captureResult;
            }
            finally
            {
                ExitOverlay();
            }
        }).Task.WaitAsync(cancellationToken);
    }

    private static bool TryEnterOverlay() => Interlocked.CompareExchange(ref _openOverlay, 1, 0) == 0;

    private static void ExitOverlay() => Interlocked.Exchange(ref _openOverlay, 0);

    private void OnSelectMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _start = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnSelectMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_editing || _start is null)
        {
            return;
        }

        _selectionGeometry.Selection = ToRect(_start.Value, e.GetPosition(this));
    }

    private void OnSelectMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_editing || _start is null || _capture is null)
        {
            return;
        }

        ReleaseMouseCapture();
        var selected = ToRect(_start.Value, e.GetPosition(this));
        _start = null;
        if (selected.Width <= 2 || selected.Height <= 2)
        {
            Close();
            return;
        }

        var bounds = ToDeviceRectangle(selected);

        Opacity = 0;
        Cursor = System.Windows.Input.Cursors.Wait;
        _selectionGeometry.Selection = Rect.Empty;
        UpdateLayout();
        Thread.Sleep(80);
        try
        {
            var capture = _capture(bounds);
            Opacity = 1;
            Activate();
            BeginEdit(capture, bounds, selected);
        }
        catch
        {
            Close();
            throw;
        }
    }

    private void BeginEdit(ScreenCaptureResult capture, Rectangle preferredBounds, Rect? exactDipBounds = null)
    {
        _editing = true;
        _captureResult = null;
        _editingCapture = capture;
        _currentDeviceBounds = preferredBounds;
        Cursor = System.Windows.Input.Cursors.Arrow;
        _selectionGeometry.Visibility = Visibility.Collapsed;
        _root.Children.Clear();
        _root.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(132, 0, 0, 0));

        var bitmap = LoadBitmap(capture);
        var actualDipBounds = ToDipRect(preferredBounds);
        var preferredDipBounds = !actualDipBounds.IsEmpty &&
                                 actualDipBounds.Width > 1 &&
                                 actualDipBounds.Height > 1
            ? actualDipBounds
            : exactDipBounds ?? actualDipBounds;
        var imageBounds = ScreenshotOverlayLayout.ImageBoundsForSelection(
            preferredDipBounds,
            new System.Windows.Size(bitmap.PixelWidth, bitmap.PixelHeight));
        _surface.Width = bitmap.PixelWidth;
        _surface.Height = bitmap.PixelHeight;
        _surface.Background = System.Windows.Media.Brushes.Transparent;
        _surface.Children.Clear();
        _baseImage = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            Stretch = Stretch.None
        };
        _surface.Children.Add(_baseImage);

        _inkCanvas.Width = bitmap.PixelWidth;
        _inkCanvas.Height = bitmap.PixelHeight;
        _inkCanvas.Background = System.Windows.Media.Brushes.Transparent;
        _inkCanvas.Strokes.Clear();
        _surface.Children.Add(_inkCanvas);

        _shapeCanvas.Width = bitmap.PixelWidth;
        _shapeCanvas.Height = bitmap.PixelHeight;
        _shapeCanvas.Background = System.Windows.Media.Brushes.Transparent;
        _shapeCanvas.Children.Clear();
        _shapeCanvas.MouseDown -= ShapeCanvas_MouseDown;
        _shapeCanvas.MouseMove -= ShapeCanvas_MouseMove;
        _shapeCanvas.MouseUp -= ShapeCanvas_MouseUp;
        _shapeCanvas.MouseDown += ShapeCanvas_MouseDown;
        _shapeCanvas.MouseMove += ShapeCanvas_MouseMove;
        _shapeCanvas.MouseUp += ShapeCanvas_MouseUp;
        _surface.Children.Add(_shapeCanvas);

        var imageView = new Viewbox
        {
            Stretch = Stretch.Fill,
            Child = _surface
        };
        var holder = new Border
        {
            Width = imageBounds.Width,
            Height = imageBounds.Height,
            Child = imageView,
            Background = System.Windows.Media.Brushes.Transparent,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Opacity = 0.45 }
        };
        holder.PreviewMouseLeftButtonDown += ImageHolder_MouseLeftButtonDown;
        holder.PreviewMouseMove += ImageHolder_MouseMove;
        holder.PreviewMouseLeftButtonUp += ImageHolder_MouseLeftButtonUp;
        _imageHolder = holder;
        _root.Children.Add(holder);

        _imageFrame = new System.Windows.Shapes.Rectangle
        {
            Stroke = ScreenshotSelectionStyle.BorderBrush,
            StrokeThickness = ScreenshotSelectionStyle.BorderThickness,
            Fill = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetEdgeMode(_imageFrame, EdgeMode.Aliased);
        _root.Children.Add(_imageFrame);
        SetImageBounds(imageBounds);

        var toolbar = BuildToolbar(capture);
        toolbar.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = toolbar.DesiredSize.Width > 0
            ? toolbar.DesiredSize
            : new System.Windows.Size(380, 44);
        var toolbarBounds = ScreenshotOverlayLayout.PlaceToolbar(
            imageBounds,
            new Rect(0, 0, ActualWidth, ActualHeight),
            toolbarSize);
        Canvas.SetLeft(toolbar, toolbarBounds.X);
        Canvas.SetTop(toolbar, toolbarBounds.Y);
        _root.Children.Add(toolbar);
        ConfigureTooling();
    }

    private Border BuildToolbar(ScreenCaptureResult capture)
    {
        var toolbar = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(6)
        };
        _toolbarButtons.Clear();

        var tools = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        tools.Children.Add(ToolButton("Move", "M12,3 V21 M12,3 L8,7 M12,3 L16,7 M12,21 L8,17 M12,21 L16,17 M3,12 H21 M3,12 L7,8 M3,12 L7,16 M21,12 L17,8 M21,12 L17,16", "Move"));
        tools.Children.Add(ToolButton("Pen", "M3,19 L14,8 L18,12 L7,23 L2,24 Z", "Pen"));
        tools.Children.Add(ToolButton("Rectangle", "M4,5 H22 V19 H4 Z", "Rectangle"));
        tools.Children.Add(ToolButton("Ellipse", "M13,5 C18,5 22,8 22,13 C22,18 18,21 13,21 C8,21 4,18 4,13 C4,8 8,5 13,5 Z", "Ellipse"));

        tools.Children.Add(Separator());
        tools.Children.Add(ColorPickerButton());

        _width.Minimum = 2;
        _width.Maximum = 18;
        _width.Value = Math.Clamp(_initialWidth, 2, 18);
        _width.Width = 58;
        _width.Height = 28;
        _width.Margin = new Thickness(5, 0, 4, 0);
        _width.VerticalAlignment = VerticalAlignment.Center;
        tools.Children.Add(_width);
        tools.Children.Add(Separator());
        tools.Children.Add(IconButton("M8,7 H4 V3 M5,7 C7,4 11,4 14,6 C17,8 18,12 16,15 C14,19 9,20 5,17", "Undo", (_, _) => Undo()));
        toolbar.Children.Add(tools);

        _statusText = new TextBlock
        {
            Text = string.Empty,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120,
            Margin = new Thickness(6, 0, 4, 0)
        };
        toolbar.Children.Add(_statusText);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        actions.Children.Add(IconButton("M5,5 H19 V21 H5 Z M8,5 V12 H16 V5 M8,18 H16", "Save", (_, _) => SaveAndClose(capture, copy: false)));
        actions.Children.Add(IconButton("M7,7 L19,19 M19,7 L7,19", "Cancel", (_, _) =>
        {
            Close();
        }));
        actions.Children.Add(IconButton("M5,13 L10,18 L20,7", "Copy to clipboard", (_, _) => SaveAndClose(capture, copy: true)));
        toolbar.Children.Add(actions);

        _toolbarContainer = new Border
        {
            Child = toolbar,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 24, 27, 34)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 85, 103)),
            BorderThickness = new Thickness(1)
        };
        return _toolbarContainer;
    }

    private void ConfigureTooling()
    {
        _width.ValueChanged -= Tooling_Changed;
        _width.ValueChanged += Tooling_Changed;
        ApplyEditingMode();
        ApplyInkAttributes();
        ApplyToolState();
    }

    private void Tooling_Changed(object sender, RoutedEventArgs e) => ApplyInkAttributes();

    private void ApplyEditingMode()
    {
        var tool = CurrentTool();
        _inkCanvas.EditingMode = tool == "Pen" ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        _inkCanvas.IsHitTestVisible = tool == "Pen";
        _shapeCanvas.IsHitTestVisible = tool is "Rectangle" or "Ellipse";
        Cursor = tool == "Move" ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Cross;
        if (_imageHolder is not null)
        {
            _imageHolder.Cursor = tool == "Move" ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Cross;
        }
    }

    private void ImageHolder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || CurrentTool() != "Move" || _imageHolder is null)
        {
            return;
        }

        _moveStart = e.GetPosition(_root);
        _moveOrigin = new Point(Canvas.GetLeft(_imageHolder), Canvas.GetTop(_imageHolder));
        _imageHolder.CaptureMouse();
        e.Handled = true;
    }

    private void ImageHolder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_moveStart is null || _moveOrigin is null || _imageHolder is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(_root);
        var dx = current.X - _moveStart.Value.X;
        var dy = current.Y - _moveStart.Value.Y;
        var movedBounds = ScreenshotOverlayLayout.MoveSelectionBounds(
            new Rect(_moveOrigin.Value.X, _moveOrigin.Value.Y, _imageHolder.Width, _imageHolder.Height),
            new Vector(dx, dy),
            new Rect(0, 0, ActualWidth, ActualHeight));
        SetImageBounds(movedBounds);
        UpdateToolbarPlacement();
        e.Handled = true;
    }

    private void ImageHolder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_moveStart is null || _imageHolder is null)
        {
            return;
        }

        _imageHolder.ReleaseMouseCapture();
        _moveStart = null;
        _moveOrigin = null;
        RefreshCaptureFromMovedBounds();
        e.Handled = true;
    }

    private void RefreshCaptureFromMovedBounds()
    {
        if (_capture is null || _baseImage is null || _imageBounds.IsEmpty)
        {
            return;
        }

        var bounds = ToDeviceRectangle(_imageBounds);
        if (bounds == _currentDeviceBounds)
        {
            return;
        }

        var previousOpacity = Opacity;
        var previousCursor = Cursor;
        try
        {
            Opacity = 0;
            Cursor = System.Windows.Input.Cursors.Wait;
            UpdateLayout();
            Thread.Sleep(80);

            var updated = _capture(bounds);
            var bitmap = LoadBitmap(updated);
            _baseImage.Source = bitmap;
            _baseImage.Width = bitmap.PixelWidth;
            _baseImage.Height = bitmap.PixelHeight;
            _surface.Width = bitmap.PixelWidth;
            _surface.Height = bitmap.PixelHeight;
            _inkCanvas.Width = bitmap.PixelWidth;
            _inkCanvas.Height = bitmap.PixelHeight;
            _shapeCanvas.Width = bitmap.PixelWidth;
            _shapeCanvas.Height = bitmap.PixelHeight;
            _editingCapture = updated;
            _currentDeviceBounds = bounds;
        }
        finally
        {
            Opacity = previousOpacity;
            Cursor = previousCursor;
            Activate();
        }
    }

    private void SetImageBounds(Rect bounds)
    {
        _imageBounds = bounds;
        if (_imageHolder is not null)
        {
            _imageHolder.Width = bounds.Width;
            _imageHolder.Height = bounds.Height;
            Canvas.SetLeft(_imageHolder, bounds.Left);
            Canvas.SetTop(_imageHolder, bounds.Top);
        }

        if (_imageFrame is not null)
        {
            _imageFrame.Width = bounds.Width;
            _imageFrame.Height = bounds.Height;
            Canvas.SetLeft(_imageFrame, bounds.Left);
            Canvas.SetTop(_imageFrame, bounds.Top);
        }
    }

    private void UpdateToolbarPlacement()
    {
        if (_toolbarContainer is null || _imageBounds.IsEmpty)
        {
            return;
        }

        _toolbarContainer.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = _toolbarContainer.DesiredSize.Width > 0
            ? _toolbarContainer.DesiredSize
            : new System.Windows.Size(380, 44);
        var toolbarBounds = ScreenshotOverlayLayout.PlaceToolbar(
            _imageBounds,
            new Rect(0, 0, ActualWidth, ActualHeight),
            toolbarSize);
        Canvas.SetLeft(_toolbarContainer, toolbarBounds.X);
        Canvas.SetTop(_toolbarContainer, toolbarBounds.Y);
    }

    private WpfButton ToolButton(string tool, string icon, string tooltip)
    {
        var button = IconButton(icon, tooltip, (_, _) =>
        {
            _currentTool = tool;
            ApplyEditingMode();
            ApplyToolState();
        });
        button.Tag = tool;
        return button;
    }

    private WpfButton ColorPickerButton()
    {
        _colorSwatch = new Border
        {
            Width = 13,
            Height = 13,
            CornerRadius = new CornerRadius(7),
            Background = BrushForName(_currentColor),
            BorderBrush = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(_currentColor == "White" ? 1 : 0)
        };
        var button = new WpfButton
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(2),
            ToolTip = "Color",
            Content = _colorSwatch,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => ShowColorMenu(button);
        button.Tag = "color";
        _toolbarButtons.Add(button);
        return button;
    }

    private void ShowColorMenu(WpfButton button)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 41, 54)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 85, 103)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            PlacementTarget = button
        };
        foreach (var color in new[] { "Red", "Yellow", "Green", "Blue", "White" })
        {
            var item = new MenuItem
            {
                Header = ColorMenuItem(color),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new Thickness(8, 5, 10, 5),
                Background = System.Windows.Media.Brushes.Transparent
            };
            item.Click += (_, _) => SelectColor(color);
            menu.Items.Add(item);
        }

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static UIElement ColorMenuItem(string color)
    {
        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        panel.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = BrushForName(color),
            BorderBrush = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(color == "White" ? 1 : 0),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = color,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void SelectColor(string color)
    {
        _currentColor = color;
        if (_colorSwatch is not null)
        {
            _colorSwatch.Background = BrushForName(color);
            _colorSwatch.BorderThickness = new Thickness(color == "White" ? 1 : 0);
        }

        _saveColor?.Invoke(color);
        ApplyInkAttributes();
        ApplyToolState();
    }

    private WpfButton IconButton(string geometry, string tooltip, RoutedEventHandler onClick)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(geometry),
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = System.Windows.Media.Brushes.Transparent,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
        var button = new WpfButton
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(4),
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = tooltip,
            Content = path,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255))
        };
        button.Click += onClick;
        _toolbarButtons.Add(button);
        return button;
    }

    private static UIElement Separator() => new Border
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(7, 0, 4, 0),
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(74, 255, 255, 255))
    };

    private void ApplyToolState()
    {
        foreach (var button in _toolbarButtons)
        {
            var active = button.Tag?.ToString() == _currentTool ||
                         button.Tag?.ToString() == "color";
            button.BorderBrush = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 210, 151))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 255, 255, 255));
        }
    }

    private void ShapeCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            CurrentTool() is not ("Rectangle" or "Ellipse"))
        {
            return;
        }

        _shapeStart = e.GetPosition(_shapeCanvas);
        _activeShape = CurrentTool() == "Rectangle"
            ? new System.Windows.Shapes.Rectangle()
            : new System.Windows.Shapes.Ellipse();
        _activeShape.Stroke = CurrentBrush();
        _activeShape.StrokeThickness = _width.Value;
        _activeShape.Fill = System.Windows.Media.Brushes.Transparent;
        Canvas.SetLeft(_activeShape, _shapeStart.Value.X);
        Canvas.SetTop(_activeShape, _shapeStart.Value.Y);
        _shapeCanvas.Children.Add(_activeShape);
        _shapeCanvas.CaptureMouse();
    }

    private void ShapeCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_shapeStart is null || _activeShape is null)
        {
            return;
        }

        var current = e.GetPosition(_shapeCanvas);
        var left = Math.Min(_shapeStart.Value.X, current.X);
        var top = Math.Min(_shapeStart.Value.Y, current.Y);
        var width = Math.Abs(current.X - _shapeStart.Value.X);
        var height = Math.Abs(current.Y - _shapeStart.Value.Y);
        Canvas.SetLeft(_activeShape, left);
        Canvas.SetTop(_activeShape, top);
        _activeShape.Width = width;
        _activeShape.Height = height;
    }

    private void ShapeCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _shapeCanvas.ReleaseMouseCapture();
        if (_activeShape is not null && (_activeShape.Width < 3 || _activeShape.Height < 3))
        {
            _shapeCanvas.Children.Remove(_activeShape);
        }

        _activeShape = null;
        _shapeStart = null;
    }

    private async void SaveAndClose(ScreenCaptureResult capture, bool copy)
    {
        if (_saving)
        {
            return;
        }

        try
        {
            _saving = true;
            var activeCapture = _editingCapture ?? capture;
            string? outputPath = null;
            if (!copy && !TryChooseSavePath(activeCapture.FilePath, out outputPath))
            {
                _saving = false;
                SetToolbarEnabled(true, string.Empty);
                return;
            }

            SetToolbarEnabled(false, copy ? "Copying..." : "Saving...");
            var bytes = RenderSurfacePngBytes();
            if (copy)
            {
                var clipboardImage = LoadBitmap(bytes);
                await SetClipboardImageAsync(clipboardImage);
                _captureResult = activeCapture with { FilePath = string.Empty, ImagePng = bytes, CopiedToClipboard = true };
            }
            else
            {
                await Task.Run(() => ScreenshotFileIO.WriteAllBytesAtomic(outputPath!, bytes));
                _captureResult = activeCapture with { FilePath = outputPath!, ImagePng = null, CopiedToClipboard = false };
            }

            Close();
        }
        catch (Exception ex)
        {
            _saving = false;
            SetToolbarEnabled(true, $"Failed: {ex.Message}");
        }
    }

    private byte[] RenderSurfacePngBytes()
    {
        _surface.Measure(new System.Windows.Size(_surface.Width, _surface.Height));
        _surface.Arrange(new Rect(0, 0, _surface.Width, _surface.Height));
        _surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)_surface.Width,
            (int)_surface.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(_surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private bool TryChooseSavePath(string currentPath, out string? outputPath)
    {
        outputPath = null;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(currentPath)
                ? $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                : Path.GetFileName(currentPath)
        };

        var directory = string.IsNullOrWhiteSpace(currentPath) ? null : Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            dialog.InitialDirectory = directory;
        }

        var wasTopmost = Topmost;
        Topmost = false;
        try
        {
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            outputPath = dialog.FileName;
            return !string.IsNullOrWhiteSpace(outputPath);
        }
        finally
        {
            Topmost = wasTopmost;
            Activate();
        }
    }

    private void SetToolbarEnabled(bool enabled, string status)
    {
        foreach (var button in _toolbarButtons)
        {
            button.IsEnabled = enabled;
        }

        _width.IsEnabled = enabled;
        if (_statusText is not null)
        {
            _statusText.Text = status;
        }
    }

    private void Undo()
    {
        if (_shapeCanvas.Children.Count > 0)
        {
            _shapeCanvas.Children.RemoveAt(_shapeCanvas.Children.Count - 1);
            return;
        }

        if (_inkCanvas.Strokes.Count > 0)
        {
            _inkCanvas.Strokes.RemoveAt(_inkCanvas.Strokes.Count - 1);
        }
    }

    private void ApplyInkAttributes()
    {
        _inkCanvas.DefaultDrawingAttributes.Color = ((SolidColorBrush)CurrentBrush()).Color;
        _inkCanvas.DefaultDrawingAttributes.Width = _width.Value;
        _inkCanvas.DefaultDrawingAttributes.Height = _width.Value;
    }

    private string CurrentTool() => _currentTool;

    private System.Windows.Media.Brush CurrentBrush() => BrushForName(_currentColor);

    private static System.Windows.Media.Brush BrushForName(string color) => color switch
    {
        "Yellow" => System.Windows.Media.Brushes.Yellow,
        "Green" => System.Windows.Media.Brushes.LimeGreen,
        "Blue" => System.Windows.Media.Brushes.DeepSkyBlue,
        "White" => System.Windows.Media.Brushes.White,
        _ => System.Windows.Media.Brushes.Red
    };

    private static Rect ToRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private Rectangle ToDeviceRectangle(Rect dipRect) =>
        ScreenshotOverlayLayout.DeviceRectangleFromPoints(
            PointToScreen(new Point(dipRect.Left, dipRect.Top)),
            PointToScreen(new Point(dipRect.Right, dipRect.Bottom)));

    private Rect ToDipRect(Rectangle deviceRect)
    {
        var topLeft = PointFromScreen(new Point(deviceRect.Left, deviceRect.Top));
        var bottomRight = PointFromScreen(new Point(deviceRect.Right, deviceRect.Bottom));
        return ToRect(topLeft, bottomRight);
    }

    private static Rect GetVirtualScreenBoundsDip()
    {
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
    }

    private static string NormalizeColorName(string value)
    {
        var allowed = new[] { "Red", "Yellow", "Green", "Blue", "White" };
        return allowed.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "Red";
    }

    private static BitmapImage LoadBitmap(ScreenCaptureResult capture)
    {
        if (capture.ImagePng is { Length: > 0 } bytes)
        {
            return LoadBitmap(bytes);
        }

        return LoadBitmap(capture.FilePath);
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage LoadBitmap(byte[] pngBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task SetClipboardImageAsync(BitmapSource image)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return;
            }
            catch (COMException ex)
            {
                lastError = ex;
            }
            catch (ExternalException ex)
            {
                lastError = ex;
            }

            await Task.Delay(60);
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }
}
