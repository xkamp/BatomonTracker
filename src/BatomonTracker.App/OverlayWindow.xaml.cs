using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BatomonTracker.Core.Models;
using BatomonTracker.Core.Recommendations;

namespace BatomonTracker.App;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const long WsExTransparent = 0x20L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x80L;
    private bool clickThrough = true;
    private HwndSource? hwndSource;
    private Rect? gameBounds;
    private readonly OverlayPositionStore positionStore;
    private readonly BatomonIconRepository iconRepository = new();
    private readonly BuildRecommendationEngine recommendationEngine = new();
    private double relativeLeft;
    private double relativeTop;
    private bool autoShowEnabled = true;

    public OverlayWindow()
    {
        positionStore = new OverlayPositionStore();
        (relativeLeft, relativeTop) = positionStore.Load();
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowMode();
    }

    public void UpdateState(RunState state)
    {
        TrainerText.Text = state.TrainerId ?? "Trainer sconosciuto";
        RoundText.Text = $"Round {state.Round?.ToString() ?? "n/d"}";
        TeamPanel.Children.Clear();
        if (state.Team.Count == 0)
        {
            TeamPanel.Children.Add(new TextBlock
            {
                Text = "In attesa del team",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11
            });
        }
        else
        {
            foreach (BatomonUnit unit in state.Team)
            {
                TeamPanel.Children.Add(CreateUnitView(unit));
            }
        }
        TrinketText.Text = state.Trinkets.Count == 0
            ? "Trinket: nessuno"
            : $"Trinket: {string.Join(", ", state.Trinkets)}";
        BuildRecommendation recommendation = recommendationEngine.Evaluate(state);
        RecommendationText.Text = recommendation.Action == RecommendationAction.Buy
            ? $"PRIORITA: {recommendation.Reason}"
            : $"PRIORITA: {recommendation.Reason}";
        RecommendedBatomonText.Text = "Analisi globale in aggiornamento...";
        RecommendedToolsText.Text = "Analisi globale in aggiornamento...";
    }

    public void UpdateGlobalRecommendation(GlobalBuildRecommendation recommendation)
    {
        RecommendedBatomonText.Text = recommendation.RecommendedSpecies.Count == 0
            ? $"Build di riferimento gia vicina ({recommendation.TeamOverlap} match, {recommendation.ReferenceWins} vittorie)."
            : string.Join(", ", recommendation.RecommendedSpecies);
        RecommendedToolsText.Text = recommendation.RecommendedTrinkets.Count == 0
            ? "Nessun trinket osservato nella build di riferimento."
            : string.Join(", ", recommendation.RecommendedTrinkets);
    }

    public void ClearGlobalRecommendation()
    {
        RecommendedBatomonText.Text = "Analisi globale non disponibile.";
        RecommendedToolsText.Text = "Analisi globale non disponibile.";
    }

    public void ClearState()
    {
        TrainerText.Text = "Nessuna run";
        RoundText.Text = "Run non attiva";
        TeamPanel.Children.Clear();
        TeamPanel.Children.Add(new TextBlock
        {
            Text = "In attesa del team",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 16
        });
        TrinketText.Text = "Trinket: nessuno";
        RecommendationText.Text = "PRIORITA: attendi una run";
        ClearGlobalRecommendation();
    }

    public void ApplySettings(OverlaySettings settings)
    {
        LayoutTransform = new ScaleTransform(settings.Scale, settings.Scale);
        Opacity = settings.Opacity;
        autoShowEnabled = settings.StartVisible;
    }

    public void ToggleInteractive()
    {
        clickThrough = !clickThrough;
        ApplyWindowMode();
        HintText.Text = clickThrough
            ? "Ctrl+Shift+O mostra/nasconde | Ctrl+Shift+L sblocca"
            : "MODIFICA ATTIVA | Ctrl+Shift+L blocca";
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void AttachToGame(Rect bounds)
    {
        gameBounds = bounds;
        Left = bounds.Left + bounds.Width * relativeLeft;
        Top = bounds.Top + bounds.Height * relativeTop;

        if (!IsVisible && autoShowEnabled)
        {
            Show();
        }
    }

    public void DetachFromGame()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!clickThrough && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            RememberRelativePosition();
        }
    }

    private void RememberRelativePosition()
    {
        if (!gameBounds.HasValue || gameBounds.Value.Width <= 0 || gameBounds.Value.Height <= 0)
        {
            return;
        }

        relativeLeft = (Left - gameBounds.Value.Left) / gameBounds.Value.Width;
        relativeTop = (Top - gameBounds.Value.Top) / gameBounds.Value.Height;
        positionStore.Save(relativeLeft, relativeTop);
    }

    private FrameworkElement CreateUnitView(BatomonUnit unit)
    {
        var content = new StackPanel
        {
                Width = 72,
                Margin = new Thickness(2, 0, 2, 0),
            ToolTip = unit.SpeciesId
        };
        BitmapImage? icon = iconRepository.Load(unit.SpeciesId, unit.IsShiny == true);
        if (icon is not null)
        {
            content.Children.Add(new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 60,
                Height = 60,
                Stretch = System.Windows.Media.Stretch.Uniform
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = unit.SpeciesId,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Height = 60
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = $"Lv{unit.Level ?? 0}{(unit.IsShiny == true ? " *" : string.Empty)}",
            Foreground = unit.IsShiny == true ? System.Windows.Media.Brushes.Gold : System.Windows.Media.Brushes.White,
            FontSize = 13,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        });
        return content;
    }

    private void ApplyWindowMode()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        style = clickThrough ? style | WsExTransparent | WsExNoActivate : style & ~WsExTransparent & ~WsExNoActivate;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));

        hwndSource ??= HwndSource.FromHwnd(handle);
        if (hwndSource is not null && !hwndSource.IsDisposed)
        {
            hwndSource.RemoveHook(WindowProc);
            hwndSource.AddHook(WindowProc);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcHitTest && clickThrough)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        return IntPtr.Zero;
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
    }

    private static void SetWindowLongPtr(IntPtr handle, int index, IntPtr value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(handle, index, value);
        }
        else
        {
            SetWindowLong32(handle, index, value.ToInt32());
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);
}
