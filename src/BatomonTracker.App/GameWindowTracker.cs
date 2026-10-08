using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BatomonTracker.App;

public sealed class GameWindowTracker : IDisposable
{
    private const string GameTitle = "Batomon Showdown";
    private readonly DispatcherTimer timer;
    private Rect? lastBounds;
    private bool started;

    public GameWindowTracker()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateBounds();
    }

    public event EventHandler<GameWindowBoundsChangedEventArgs>? BoundsChanged;

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        UpdateBounds();
        timer.Start();
    }

    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= (_, _) => UpdateBounds();
        started = false;
    }

    private void UpdateBounds()
    {
        Rect? bounds = FindGameWindowBounds();
        if (AreEqual(lastBounds, bounds))
        {
            return;
        }

        lastBounds = bounds;
        BoundsChanged?.Invoke(this, new GameWindowBoundsChangedEventArgs(bounds));
    }

    private static Rect? FindGameWindowBounds()
    {
        Rect? result = null;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
            {
                return true;
            }

            int length = GetWindowTextLength(handle);
            if (length == 0)
            {
                return true;
            }

            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            if (!title.ToString().Contains(GameTitle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (GetWindowRect(handle, out NativeRect nativeRect))
            {
                result = new Rect(
                    nativeRect.Left,
                    nativeRect.Top,
                    nativeRect.Right - nativeRect.Left,
                    nativeRect.Bottom - nativeRect.Top);
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool AreEqual(Rect? first, Rect? second)
    {
        if (!first.HasValue || !second.HasValue)
        {
            return first.HasValue == second.HasValue;
        }

        return Math.Abs(first.Value.Left - second.Value.Left) < 0.5 &&
               Math.Abs(first.Value.Top - second.Value.Top) < 0.5 &&
               Math.Abs(first.Value.Width - second.Value.Width) < 0.5 &&
               Math.Abs(first.Value.Height - second.Value.Height) < 0.5;
    }

    private delegate bool EnumWindowsDelegate(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public sealed class GameWindowBoundsChangedEventArgs : EventArgs
{
    public GameWindowBoundsChangedEventArgs(Rect? bounds)
    {
        Bounds = bounds;
    }

    public Rect? Bounds { get; }
}
