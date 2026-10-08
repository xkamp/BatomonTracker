using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using BatomonTracker.Core.Analytics;
using BatomonTracker.Core.Diff;
using BatomonTracker.Core.Models;
using BatomonTracker.Core.Parsing;
using BatomonTracker.Infrastructure.Filesystem;
using BatomonTracker.Infrastructure.Analytics;
using BatomonTracker.Infrastructure.Monitoring;
using BatomonTracker.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace BatomonTracker.App;

public partial class MainWindow : Window
{
    private const int WmHotKey = 0x0312;
    private const int ToggleOverlayHotKeyId = 1;
    private const int ToggleInteractionHotKeyId = 2;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkO = 0x4F;
    private const uint VkL = 0x4C;
    private readonly RunSaveMonitor monitor;
    private readonly SqliteRunStore store;
    private readonly OverlayWindow overlay;
    private readonly OverlaySettingsStore overlaySettingsStore;
    private OverlaySettings overlaySettings;
    private readonly Forms.NotifyIcon trayIcon;
    private bool loadingSettingsControls;
    private readonly GameWindowTracker gameWindowTracker;
    private readonly SteamAuthListener steamAuthListener;
    private readonly AuthSessionStore authSessionStore;
    private readonly RemoteSyncClient remoteSyncClient = new();
    private readonly OpponentCacheImporter opponentCacheImporter = new();
    private readonly OpponentAnalyticsCalculator opponentAnalyticsCalculator = new();
    private readonly BatomonIconRepository homeIconRepository = new();
    private readonly CustomBuildStore customBuildStore = new();
    private string? lastGlobalRecommendationKey;
    private IReadOnlyList<CompletedRun> lastCompletedRuns = Array.Empty<CompletedRun>();

    public MainWindow()
    {
        InitializeComponent();
        UpdateProfileMenu(false);
        GodotSaveLocator locator = new();
        monitor = new RunSaveMonitor(locator, new SafeJsonReader(), new StateDiff());
        overlay = new OverlayWindow();
        overlaySettingsStore = new OverlaySettingsStore();
        overlaySettings = overlaySettingsStore.Load();
        overlay.ApplySettings(overlaySettings);
        trayIcon = CreateTrayIcon();
        gameWindowTracker = new GameWindowTracker();
        steamAuthListener = new SteamAuthListener();
        authSessionStore = new AuthSessionStore();
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BatomonTracker");
        store = new SqliteRunStore(Path.Combine(dataDirectory, "tracker.db"));
        monitor.StatusChanged += OnStatusChanged;
        monitor.StateUnavailable += OnStateUnavailable;
        monitor.StateUpdated += OnStateUpdated;
        Closed += (_, _) => monitor.Dispose();
        Closed += (_, _) => gameWindowTracker.Dispose();
        Closed += (_, _) => steamAuthListener.Dispose();
        Closed += (_, _) => trayIcon.Dispose();
        Loaded += OnLoaded;
        SourceInitialized += RegisterHotKeys;
        Closed += (_, _) => UnregisterHotKeys();
        steamAuthListener.Authenticated += OnSteamAuthenticated;
        LoadCurrentProfile();
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            await store.InitializeAsync();
            await ImportProfileHistoryAsync();
            monitor.Start();
            gameWindowTracker.BoundsChanged += OnGameWindowBoundsChanged;
            gameWindowTracker.Start();
            await LoadHistoryAsync();
            if (authSessionStore.Load() is AuthSessionData session)
            {
                ShowAuthenticatedDashboard(session.SteamId);
                _ = ImportProfileHistoryAsync();
                _ = SyncLiveRunAsync(session.AccessToken);
                _ = SyncOpponentCacheAsync(session.AccessToken);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            StatusText.Text = $"Persistenza locale non disponibile: {exception.Message}";
            monitor.Start();
            gameWindowTracker.BoundsChanged += OnGameWindowBoundsChanged;
            gameWindowTracker.Start();
        }
    }

    private void LoadCurrentProfile()
    {
        GodotSaveLocator locator = new();
        var account = locator.FindActiveAccount();

        if (account is null)
        {
            StatusText.Text = "Nessun account Batomon trovato.";
            return;
        }

        StatusText.Text = account.HasLiveRun
            ? "Run live rilevata."
            : "Account trovato, nessuna run live attiva.";

        if (account.ProfileSnapshotPath is null)
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(account.ProfileSnapshotPath);
            var profile = new ProfileSnapshotParser().Parse(json);
            ProfileText.Text = $"Profilo: {profile.DisplayName ?? "sconosciuto"}\n" +
                               $"Partite: {profile.GamesPlayed?.ToString() ?? "n/d"} | " +
                               $"Vittorie: {profile.RunsWon?.ToString() ?? "n/d"} | " +
                               $"Storico disponibile: {profile.MatchCount}";
        }
        catch (IOException)
        {
            ProfileText.Text = "Il profilo non e leggibile in questo momento.";
        }
        catch (System.Text.Json.JsonException)
        {
            ProfileText.Text = "Il profilo contiene JSON non valido.";
        }
    }

    private void OnStatusChanged(object? sender, MonitorStatusChangedEventArgs args)
    {
        Dispatcher.Invoke(() => StatusText.Text = args.Message);
    }

    private void OnStateUnavailable(object? sender, RunStateUnavailableEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = args.Message;
            LiveText.Text = "Nessuna run live rilevata.";
            overlay.ClearState();
        });
    }

    private void OnGameWindowBoundsChanged(object? sender, GameWindowBoundsChangedEventArgs args)
    {
        if (args.Bounds.HasValue)
        {
            overlay.AttachToGame(args.Bounds.Value);
        }
        else
        {
            overlay.DetachFromGame();
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostra dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Mostra/nascondi overlay", null, (_, _) => overlay.ToggleVisibility());
        menu.Items.Add("Blocca/sblocca overlay", null, (_, _) => overlay.ToggleInteractive());

        var scaleMenu = new Forms.ToolStripMenuItem("Scala overlay");
        AddScaleItem(scaleMenu, "75%", 0.75);
        AddScaleItem(scaleMenu, "100%", 1.0);
        AddScaleItem(scaleMenu, "125%", 1.25);
        AddScaleItem(scaleMenu, "150%", 1.5);
        scaleMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        var opacityMenu = new Forms.ToolStripMenuItem("Opacita overlay");
        AddOpacityItem(opacityMenu, "60%", 0.60);
        AddOpacityItem(opacityMenu, "80%", 0.80);
        AddOpacityItem(opacityMenu, "95%", 0.95);
        AddOpacityItem(opacityMenu, "100%", 1.0);
        scaleMenu.DropDownItems.Add(opacityMenu);
        menu.Items.Add(scaleMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => Close());

        return new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "Batomon Tracker",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private void AddScaleItem(Forms.ToolStripMenuItem parent, string text, double scale)
    {
        parent.DropDownItems.Add(text, null, (_, _) =>
        {
            overlaySettings = overlaySettings with { Scale = scale };
            overlaySettingsStore.Save(overlaySettings);
            overlay.ApplySettings(overlaySettings);
        });
    }

    private void AddOpacityItem(Forms.ToolStripMenuItem parent, string text, double opacity)
    {
        parent.DropDownItems.Add(text, null, (_, _) =>
        {
            overlaySettings = overlaySettings with { Opacity = opacity };
            overlaySettingsStore.Save(overlaySettings);
            overlay.ApplySettings(overlaySettings);
        });
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnStateUpdated(object? sender, RunStateUpdatedEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            RunState state = args.State;
            string team = state.Team.Count == 0
                ? "vuoto"
                : string.Join(", ", state.Team.Select(unit => $"{unit.SpeciesId} Lv{unit.Level ?? 0}"));
            string bench = state.Bench.Count == 0
                ? "vuota"
                : string.Join(", ", state.Bench.Select(unit => $"{unit.SpeciesId} Lv{unit.Level ?? 0}"));
            string shop = string.Join(", ", state.Shop.Select(slot => slot.Unit?.SpeciesId ?? slot.ItemId ?? "vuoto"));
            LiveText.Text = $"Run {state.RunId} | Round {state.Round?.ToString() ?? "n/d"}\n" +
                            $"Vite {state.Lives?.ToString() ?? "n/d"} | Vittorie {state.Wins?.ToString() ?? "n/d"} | " +
                            $"Oro {state.Gold?.ToString() ?? "n/d"}\n" +
                            $"Team: {team}\nShop: {shop}";
            LiveText.Text += $"\nPanchina: {bench}";
            TrinketText.Text = state.Trinkets.Count == 0
                ? "Trinket: nessuno"
                : $"Trinket: {string.Join(", ", state.Trinkets)}";
            overlay.UpdateState(state);
            UpdateLiveOverlayState(state);
            _ = UpdateGlobalOverlayRecommendationAsync(state);
        });

        try
        {
            await store.SaveAsync(args.State, args.Events);
            await LoadHistoryAsync();
        }
        catch (IOException exception)
        {
            Dispatcher.Invoke(() => StatusText.Text = $"Salvataggio locale non riuscito: {exception.Message}");
        }
    }

    private async Task UpdateGlobalOverlayRecommendationAsync(RunState state)
    {
        string[] team = state.Team.Select(unit => unit.SpeciesId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
        string key = string.Join("|", team);
        if (team.Length == 0 || string.Equals(key, lastGlobalRecommendationKey, StringComparison.Ordinal))
        {
            return;
        }

        lastGlobalRecommendationKey = key;
        try
        {
            GlobalBuildRecommendation recommendation = await remoteSyncClient.GetGlobalBuildRecommendationAsync(team, state.Trinkets);
            Dispatcher.Invoke(() =>
            {
                overlay.UpdateGlobalRecommendation(recommendation);
                UpdateLiveOverlayRecommendation(recommendation);
            });
        }
        catch (HttpRequestException)
        {
            lastGlobalRecommendationKey = null;
            Dispatcher.Invoke(() =>
            {
                overlay.ClearGlobalRecommendation();
                LiveOverlayToolsText.Text = "Analisi globale non disponibile.";
                LiveOverlayReferenceText.Text = "API globale non disponibile.";
            });
        }
        catch (TaskCanceledException)
        {
            lastGlobalRecommendationKey = null;
            Dispatcher.Invoke(() =>
            {
                overlay.ClearGlobalRecommendation();
                LiveOverlayToolsText.Text = "Analisi globale non disponibile.";
                LiveOverlayReferenceText.Text = "Timeout API globale.";
            });
        }
        catch (JsonException)
        {
            lastGlobalRecommendationKey = null;
            Dispatcher.Invoke(() =>
            {
                overlay.ClearGlobalRecommendation();
                LiveOverlayToolsText.Text = "Analisi globale non disponibile.";
                LiveOverlayReferenceText.Text = "Risposta API non valida.";
            });
        }
    }

    private void UpdateLiveOverlayState(RunState state)
    {
        LiveOverlayStatusText.Text = $"Round {state.Round?.ToString() ?? "n/d"} · {state.Wins?.ToString() ?? "0"} vittorie · {state.Gold?.ToString() ?? "0"} oro";
        RenderMainUnitPanel(LiveOverlayTeamPanel, state.Team.Select(unit => (unit.SpeciesId, unit.Level)));
    }

    private void UpdateLiveOverlayRecommendation(GlobalBuildRecommendation recommendation)
    {
        RenderMainUnitPanel(LiveOverlayRecommendedPanel, recommendation.RecommendedSpecies.Select(speciesId => (speciesId, (int?)null)));
        LiveOverlayReferenceText.Text = recommendation.ReferenceTeam.Count == 0
            ? "Nessuna composizione globale compatibile trovata."
            : $"Riferimento: {string.Join(" · ", recommendation.ReferenceTeam)}\n" +
              $"Match: {recommendation.TeamOverlap} · Vittorie: {recommendation.ReferenceWins}";
        LiveOverlayToolsText.Text = recommendation.RecommendedTrinkets.Count == 0
            ? "Nessun trinket disponibile nella composizione di riferimento."
            : string.Join("\n", recommendation.RecommendedTrinkets.Select(tool => $"- {tool}"));
    }

    private void RenderMainUnitPanel(System.Windows.Controls.Panel panel, IEnumerable<(string SpeciesId, int? Level)> units)
    {
        panel.Children.Clear();
        foreach ((string speciesId, int? level) in units)
        {
            var card = new StackPanel { Width = 78, Margin = new Thickness(0, 0, 10, 8), ToolTip = speciesId };
            System.Windows.Media.Imaging.BitmapImage? icon = homeIconRepository.Load(speciesId, false);
            if (icon is not null)
            {
                card.Children.Add(new System.Windows.Controls.Image { Source = icon, Width = 56, Height = 56, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            }
            card.Children.Add(new TextBlock { Text = speciesId, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233d4d")) });
            if (level.HasValue)
            {
                card.Children.Add(new TextBlock { Text = $"Lv{level}", FontSize = 11, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#619b8a")) });
            }
            panel.Children.Add(card);
        }
        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nessun Batomon nel team.", Foreground = System.Windows.Media.Brushes.Gray });
        }
    }

    private async void RefreshHistory_Click(object sender, RoutedEventArgs args)
    {
        await LoadHistoryAsync();
    }

    private void OfflineLogin_Click(object sender, RoutedEventArgs args)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
        ConnectionText.Text = "LOCAL MODE";
        UpdateProfileMenu(false);
    }

    private void SteamLogin_Click(object sender, RoutedEventArgs args)
    {
        string authUrl = Environment.GetEnvironmentVariable("BATOMON_AUTH_URL") ?? "http://localhost:5000/auth/steam/login";
        try
        {
            string callback = steamAuthListener.Start();
            string separator = authUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            string loginUrl = $"{authUrl}{separator}return_url={Uri.EscapeDataString(callback)}&state={Uri.EscapeDataString(steamAuthListener.CurrentState)}";
            Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
            LoginStatusText.Text = "Si e aperta la pagina di accesso Steam nel browser.";
        }
        catch (HttpListenerException exception)
        {
            LoginStatusText.Text = $"Impossibile avviare il callback locale: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            LoginStatusText.Text = $"Impossibile aprire Steam: {exception.Message}";
        }
    }

    private void Navigation_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string page)
        {
            return;
        }

        NavigateToPage(page);
    }

    private void NavigateToPage(string page)
    {

        LoginPanel.Visibility = Visibility.Collapsed;
        AnalyticsPanel.Visibility = page == "Analytics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "Impostazioni" ? Visibility.Visible : Visibility.Collapsed;
        PatchNotesPanel.Visibility = page == "Patch notes" ? Visibility.Visible : Visibility.Collapsed;
        LiveOverlayPanel.Visibility = page == "Live Overlay" ? Visibility.Visible : Visibility.Collapsed;
        RunsPanel.Visibility = page == "Le mie run" ? Visibility.Visible : Visibility.Collapsed;
        BuildsPanel.Visibility = page == "Le mie build" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = page == "Analytics" || page == "Impostazioni" || page == "Patch notes" || page == "Live Overlay" || page == "Le mie run" || page == "Le mie build" ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = page;
        PageSubtitle.Text = page == "Home"
            ? "Il tuo centro di controllo per Batomon Showdown"
            : "Questa sezione verra collegata nel prossimo incremento.";

        if (page == "Analytics")
        {
            _ = LoadAnalyticsAsync();
        }
        else if (page == "Impostazioni")
        {
            LoadSettingsControls();
        }
    }

    private void PatchNotesTeaser_Click(object sender, RoutedEventArgs args)
    {
        NavigateToPage("Patch notes");
    }

    private void LoadSettingsControls()
    {
        loadingSettingsControls = true;
        OverlayScaleSlider.Value = overlaySettings.Scale;
        OverlayOpacitySlider.Value = overlaySettings.Opacity;
        OverlayStartVisibleCheckBox.IsChecked = overlaySettings.StartVisible;
        UpdateSettingsLabels();
        loadingSettingsControls = false;
    }

    private void OverlayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        UpdateSettingsLabels();
        SaveSettingsFromControls();
    }

    private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        UpdateSettingsLabels();
        SaveSettingsFromControls();
    }

    private void OverlayStartVisibleCheckBox_Changed(object sender, RoutedEventArgs args)
    {
        SaveSettingsFromControls();
    }

    private void UpdateSettingsLabels()
    {
        if (OverlayScaleValueText is not null && OverlayOpacityValueText is not null && OverlayScaleSlider is not null && OverlayOpacitySlider is not null)
        {
            OverlayScaleValueText.Text = $"{OverlayScaleSlider.Value:P0}";
            OverlayOpacityValueText.Text = $"{OverlayOpacitySlider.Value:P0}";
        }
    }

    private void SaveSettingsFromControls()
    {
        if (loadingSettingsControls || overlaySettingsStore is null || OverlayScaleSlider is null || OverlayOpacitySlider is null || OverlayStartVisibleCheckBox is null)
        {
            return;
        }

        overlaySettings = overlaySettings with
        {
            Scale = OverlayScaleSlider.Value,
            Opacity = OverlayOpacitySlider.Value,
            StartVisible = OverlayStartVisibleCheckBox.IsChecked == true
        };
        overlaySettingsStore.Save(overlaySettings);
        overlay.ApplySettings(overlaySettings);
    }

    private async void AnalyticsSearch_Click(object sender, RoutedEventArgs args)
    {
        await LoadAnalyticsAsync();
    }

    private async Task LoadAnalyticsAsync()
    {
        string speciesId = AnalyticsSpeciesTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(speciesId))
        {
            AnalyticsStatusText.Text = "Inserisci un species_id, ad esempio boomagon.";
            return;
        }

        string phaseValue = (AnalyticsPhaseComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        OpponentAnalyticsPhase phase = phaseValue switch
        {
            "standard" => OpponentAnalyticsPhase.Standard,
            "extended" => OpponentAnalyticsPhase.Extended,
            _ => OpponentAnalyticsPhase.All
        };

        try
        {
            AnalyticsStatusText.Text = "Lettura opponent_cache in corso...";
            OpponentCacheImportResult imported = await opponentCacheImporter.ImportAsync(new GodotSaveLocator().OpponentCacheDirectory);
            BatomonAnalyticsResult result = opponentAnalyticsCalculator.CalculateBatomon(speciesId, imported.Snapshots, imported.Runs, phase);
            IReadOnlyList<BatomonCompanionResult> companions = opponentAnalyticsCalculator.CalculateCompanions(speciesId, imported.Snapshots, imported.Runs, phase);
            IReadOnlyList<RoundAnalyticsResult> rounds = opponentAnalyticsCalculator.CalculateRounds(imported.Snapshots, phase);

            AnalyticsSummaryText.Text =
                $"Battle osservate: {result.BattleCount:N0}\n" +
                $"Battle vinte: {result.BattleWins:N0}\n" +
                $"Battle winrate: {result.BattleWinRate:P1}\n\n" +
                $"Run osservate: {result.RunCount:N0}\n" +
                $"Run complete: {result.CompletedRunCount:N0}\n" +
                $"Run raggiunte a 10 vittorie: {result.RunWins:N0}\n" +
                $"Run winrate: {result.RunWinRate:P1}";
            AnalyticsCompanionList.ItemsSource = companions
                .Take(25)
                .Select(companion =>
                    $"{companion.SpeciesId}\n" +
                    $"Run condivise: {companion.SharedRunCount:N0} · " +
                    $"Complete: {companion.CompletedSharedRunCount:N0} · " +
                    $"Winrate run: {companion.SharedRunWinRate:P1}")
                .ToArray();
            AnalyticsRoundList.ItemsSource = rounds
                .Select(round => $"Round {round.Round}: {round.BattleWinRate:P1} · {round.BattleWins}/{round.BattleCount} battle vinte")
                .ToArray();
            AnalyticsStatusText.Text = imported.FailedFiles.Count == 0
                ? $"Dataset letti: {imported.Files.Count} · Snapshot: {imported.Snapshots.Count:N0} · Fase: {phaseValue}"
                : $"Import parziale: {imported.FailedFiles.Count} file non leggibili.";
        }
        catch (DirectoryNotFoundException)
        {
            AnalyticsStatusText.Text = "Opponent cache non trovata. Avvia Batomon Showdown almeno una volta.";
            AnalyticsCompanionList.ItemsSource = Array.Empty<string>();
        }
        catch (IOException exception)
        {
            AnalyticsStatusText.Text = $"Cache non leggibile: {exception.Message}";
        }
        catch (System.Text.Json.JsonException exception)
        {
            AnalyticsStatusText.Text = $"JSON analytics non valido: {exception.Message}";
        }
    }

    private void OnSteamAuthenticated(object? sender, SteamAuthenticatedEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            authSessionStore.SaveSession(args.SteamId, args.AccessToken);
            ShowAuthenticatedDashboard(args.SteamId);
            _ = ImportProfileHistoryAsync();
            _ = SyncLiveRunAsync(args.AccessToken);
            _ = SyncOpponentCacheAsync(args.AccessToken);
            LoginStatusText.Text = "Accesso Steam completato.";
        });
    }

    private void ShowAuthenticatedDashboard(string steamId)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
        ConnectionText.Text = $"STEAM CONNECTED · {steamId}";
        ConnectionText.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A1C181"));
        UpdateProfileMenu(true);
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs args)
    {
        ProfileMenu.PlacementTarget = ProfileButton;
        ProfileMenu.IsOpen = true;
    }

    private void ProfileLogin_Click(object sender, RoutedEventArgs args)
    {
        SteamLogin_Click(sender, args);
    }

    private void ProfileLogout_Click(object sender, RoutedEventArgs args)
    {
        authSessionStore.Clear();
        steamAuthListener.Dispose();
        ProfileMenu.IsOpen = false;
        DashboardPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        ConnectionText.Text = "LOCAL MODE";
        LoginStatusText.Text = "Sessione Steam chiusa. I dati locali sono rimasti intatti.";
        UpdateProfileMenu(false);
    }

    private void UpdateProfileMenu(bool authenticated)
    {
        ProfileLoginMenuItem.IsEnabled = !authenticated;
        ProfileLogoutMenuItem.IsEnabled = authenticated;
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            IReadOnlyList<StoredRunSummary> summaries = await store.GetRunSummariesAsync();
            IReadOnlyList<CompletedRun> completedRuns = await store.GetCompletedRunsAsync();
            lastCompletedRuns = completedRuns;
            await LoadGlobalHomeStatsAsync();
            string[] liveRows = summaries.Select(summary =>
                $"{summary.LastSeen.ToLocalTime():g} | {summary.TrainerId ?? "trainer n/d"}\n" +
                $"[LIVE] Run {summary.RunId} | Round {summary.Round?.ToString() ?? "n/d"} | " +
                $"Wins {summary.Wins?.ToString() ?? "n/d"} | Lives {summary.Lives?.ToString() ?? "n/d"} | " +
                $"Snapshot {summary.SnapshotCount} | Eventi {summary.EventCount}").ToArray();
            string[] completedRows = completedRuns.Select(run =>
                $"{run.EndedAt?.ToLocalTime():g} | {run.TrainerId ?? "trainer n/d"}\n" +
                $"[COMPLETATA] Run {run.RunId} | Round {run.Round?.ToString() ?? "n/d"} | " +
                $"Wins {run.Wins?.ToString() ?? "n/d"} | Lives {run.Lives?.ToString() ?? "n/d"} | " +
                $"Esito {run.Outcome ?? "n/d"}").ToArray();
            string[] rows = completedRows.Concat(liveRows).ToArray();

            Dispatcher.Invoke(() =>
            {
                HistoryList.ItemsSource = rows;
            RenderHistoryCards(completedRuns, summaries);
            RenderBuilds(completedRuns);
            });
        }
        catch (SqliteException exception)
        {
            Dispatcher.Invoke(() => StatusText.Text = $"Storico non disponibile: {exception.Message}");
        }
    }

    private void RenderHistoryCards(IReadOnlyList<CompletedRun> completedRuns, IReadOnlyList<StoredRunSummary> liveRuns)
    {
        HistoryCardsPanel.Children.Clear();
        if (completedRuns.Count == 0 && liveRuns.Count == 0)
        {
            HistoryCardsPanel.Children.Add(new TextBlock { Text = "Non hai ancora run nello storico.", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 16 });
            return;
        }

        foreach (CompletedRun run in completedRuns)
        {
            bool won = (run.Wins ?? 0) >= 10 || string.Equals(run.Outcome, "won", StringComparison.OrdinalIgnoreCase);
            bool lost = IsLostRun(run);
            int wins = run.Wins ?? 0;
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(20),
                Background = CreateRunBackground(won, lost, wins),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(won ? "#619B8A" : lost ? "#C45B55" : wins > 5 ? "#C45B55" : "#D6A72C")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

            var result = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            result.Children.Add(new TextBlock { Text = won ? "VITTORIA" : "RUN CHIUSA", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(won ? "#619B8A" : "#9A7B52")) });
            if (wins > 0)
            {
                result.Children.Add(new TextBlock { Text = $"{wins} vittorie", Margin = new Thickness(0, 5, 0, 0), FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233D4D")) });
            }
            result.Children.Add(new TextBlock { Text = run.EndedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? "Data non disponibile", Margin = new Thickness(0, 4, 0, 0), Foreground = System.Windows.Media.Brushes.Gray });
            Grid.SetColumn(result, 0);
            layout.Children.Add(result);

            var team = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (string speciesId in ParseSpecies(run.TeamJson).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                System.Windows.Media.Imaging.BitmapImage? icon = homeIconRepository.Load(speciesId, false);
                var unit = new StackPanel { Width = 66, Margin = new Thickness(2), ToolTip = speciesId };
                if (icon is not null)
                {
                    unit.Children.Add(new System.Windows.Controls.Image { Source = icon, Width = 46, Height = 46, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
                }
                unit.Children.Add(new TextBlock { Text = speciesId, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233D4D")) });
                team.Children.Add(unit);
            }
            Grid.SetColumn(team, 1);
            layout.Children.Add(team);

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock { Text = run.TrainerId ?? "Trainer n/d", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233D4D")) });
            details.Children.Add(new TextBlock { Text = $"Round {run.Round?.ToString() ?? "n/d"} · {run.Outcome ?? "esito n/d"}", Margin = new Thickness(0, 5, 0, 0), Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(details, 2);
            layout.Children.Add(details);

            card.Child = layout;
            HistoryCardsPanel.Children.Add(card);
        }

        foreach (StoredRunSummary run in liveRuns)
        {
            var liveCard = new Border { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(20), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E3D6")), CornerRadius = new CornerRadius(12) };
            liveCard.Child = new TextBlock { Text = $"LIVE  ·  {run.RunId}\n{run.TrainerId ?? "Trainer n/d"}  ·  Round {run.Round?.ToString() ?? "n/d"}  ·  {run.Wins ?? 0} vittorie  ·  {run.SnapshotCount} snapshot", FontSize = 15, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233D4D")), TextWrapping = TextWrapping.Wrap };
            HistoryCardsPanel.Children.Add(liveCard);
        }
    }

    private void RenderBuilds(IReadOnlyList<CompletedRun> runs)
    {
        BestBuildsPanel.Children.Clear();
        var bestRuns = runs
            .Where(run => (run.Wins ?? 0) >= 10)
            .Select(run => new { Run = run, Species = ParseSpecies(run.TeamJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() })
            .Where(item => item.Species.Length > 0)
            .GroupBy(item => string.Join("|", item.Species.OrderBy(speciesId => speciesId)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Run.IsRanked == true)
                .ThenBy(item => 10 - Math.Clamp(item.Run.Lives ?? 10, 0, 10))
                .ThenByDescending(item => item.Run.Wins ?? 0)
                .First())
            .OrderByDescending(item => item.Run.IsRanked == true)
            .ThenBy(item => 10 - Math.Clamp(item.Run.Lives ?? 10, 0, 10))
            .ThenByDescending(item => item.Run.Wins ?? 0)
            .Take(3)
            .ToArray();

        if (bestRuns.Length == 0)
        {
            BestBuildsPanel.Children.Add(new TextBlock { Text = "Non hai ancora una build arrivata a 10 vittorie.", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 15 });
        }
        else
        {
            for (int index = 0; index < bestRuns.Length; index++)
            {
                var item = bestRuns[index];
                BestBuildsPanel.Children.Add(CreateBuildCard($"0{index + 1}", item.Species, item.Run));
            }
        }

        CustomBuildsPanel.Children.Clear();
        IReadOnlyList<CustomBuild> customBuilds = customBuildStore.Load();
        if (customBuilds.Count == 0)
        {
            CustomBuildsPanel.Children.Add(new TextBlock { Text = "Nessuna build salvata.", Foreground = System.Windows.Media.Brushes.Gray });
        }
        else
        {
            foreach (CustomBuild build in customBuilds.Reverse())
            {
                CustomBuild? custom = build;
                CustomBuildsPanel.Children.Add(CreateBuildCard("CUSTOM", custom.SpeciesIds, null, custom.Name));
            }
        }
    }

    private Border CreateBuildCard(string rank, IReadOnlyList<string> species, CompletedRun? run, string? customName = null)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(16),
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF")),
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E3D6")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        var rankText = new TextBlock { Text = rank, FontSize = 25, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FE7F2D")) };
        Grid.SetColumn(rankText, 0);
        layout.Children.Add(rankText);
        var team = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (string speciesId in species)
        {
            System.Windows.Media.Imaging.BitmapImage? icon = homeIconRepository.Load(speciesId, false);
            var unit = new StackPanel { Width = 64, Margin = new Thickness(2), ToolTip = speciesId };
            if (icon is not null)
            {
                unit.Children.Add(new System.Windows.Controls.Image { Source = icon, Width = 44, Height = 44, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            }
            unit.Children.Add(new TextBlock { Text = speciesId, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233D4D")) });
            team.Children.Add(unit);
        }
        Grid.SetColumn(team, 1);
        layout.Children.Add(team);
        string details = customName ?? (run is null ? "" : $"{run.Wins} vittorie · {(run.IsRanked == true ? "Ranked" : "Standard")}\n{run.EndedAt?.ToLocalTime():dd MMM yyyy}");
        var detailText = new TextBlock { Text = details, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#536873")) };
        Grid.SetColumn(detailText, 2);
        layout.Children.Add(detailText);
        card.Child = layout;
        return card;
    }

    private void SaveCustomBuild_Click(object sender, RoutedEventArgs args)
    {
        string name = BuildNameTextBox.Text.Trim();
        string[] species = BuildSpeciesTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (string.IsNullOrWhiteSpace(name) || species.Length == 0)
        {
            StatusText.Text = "Inserisci un nome e almeno un Batomon per salvare la build.";
            return;
        }

        List<CustomBuild> builds = customBuildStore.Load().ToList();
        builds.Add(new CustomBuild(name, species, DateTimeOffset.UtcNow));
        customBuildStore.Save(builds);
        BuildNameTextBox.Clear();
        BuildSpeciesTextBox.Clear();
        RenderBuilds(lastCompletedRuns);
        StatusText.Text = $"Build '{name}' salvata.";
    }

    private static bool IsLostRun(CompletedRun run)
    {
        return string.Equals(run.Outcome, "lost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(run.Outcome, "loss", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(run.Outcome, "defeat", StringComparison.OrdinalIgnoreCase) ||
               run.Lives == 0;
    }

    private static System.Windows.Media.Brush CreateRunBackground(bool won, bool lost, int wins)
    {
        if (won)
        {
            return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCEEDB"));
        }

        if (lost)
        {
            return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5D9D5"));
        }

        if (wins > 5)
        {
            var split = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            split.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCEEDB"), 0));
            split.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCEEDB"), 0.5));
            split.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5D9D5"), 0.5));
            split.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5D9D5"), 1));
            return split;
        }

        return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF0BF"));
    }

    private async Task LoadGlobalHomeStatsAsync()
    {
        try
        {
            IReadOnlyList<GlobalBatomonStat> played = await remoteSyncClient.GetGlobalTopBatomonAsync("played");
            IReadOnlyList<GlobalBatomonStat> wins = await remoteSyncClient.GetGlobalTopBatomonAsync("wins");
            GlobalBestTeam bestTeam = await remoteSyncClient.GetGlobalBestTeamAsync();
            GlobalBestCombo bestCombo = await remoteSyncClient.GetGlobalBestComboAsync();

            Dispatcher.Invoke(() =>
            {
                RenderGlobalPodium(TopPlayedPodium, played, stat => stat.RunCount);
                RenderGlobalPodium(TopWinsPodium, wins, stat => stat.RunWins);
                BestComboText.Text = bestCombo.FirstSpeciesId is not null && bestCombo.SecondSpeciesId is not null
                    ? $"{bestCombo.FirstSpeciesId}  +  {bestCombo.SecondSpeciesId}"
                    : "Nessuna combo disponibile";
                BestComboDetailText.Text = bestCombo.SharedRuns > 0
                    ? $"Presenti insieme in {bestCombo.SharedRuns} run globali arrivate a 10 vittorie."
                    : "Il server non ha ancora abbastanza run vinte per calcolare una combo.";
                if (bestTeam.Team.Count > 0)
                {
                    BestTeamText.Text = string.Join("  ·  ", bestTeam.Team);
                    BestTeamDetailText.Text = $"Analytics globali · {bestTeam.Wins ?? 10} vittorie raggiunte";
                }
                else
                {
                    BestTeamText.Text = "Nessuna squadra globale disponibile.";
                    BestTeamDetailText.Text = "Il server non ha ancora una run arrivata a 10 vittorie.";
                }
            });
        }
        catch (HttpRequestException)
        {
            SetGlobalHomeUnavailable("Analytics globali non disponibili: API offline.");
        }
        catch (TaskCanceledException)
        {
            SetGlobalHomeUnavailable("Analytics globali non disponibili: timeout API.");
        }
        catch (JsonException)
        {
            SetGlobalHomeUnavailable("Analytics globali non disponibili: risposta API non valida.");
        }
    }

    private void SetGlobalHomeUnavailable(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TopPlayedPodium.Children.Clear();
            TopWinsPodium.Children.Clear();
            TopPlayedPodium.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });
            TopWinsPodium.Children.Add(new TextBlock { Text = "Avvia il server per vedere il podio globale.", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray });
            BestComboText.Text = "Analytics globali offline";
            BestComboDetailText.Text = "Nessun dato personale viene usato come fallback.";
            BestTeamText.Text = "Squadra globale non disponibile.";
            BestTeamDetailText.Text = message;
        });
    }

    private void RenderGlobalPodium(StackPanel panel, IEnumerable<GlobalBatomonStat> entries, Func<GlobalBatomonStat, long> valueSelector)
    {
        panel.Children.Clear();
        GlobalBatomonStat[] podium = entries.Take(3).ToArray();
        if (podium.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nessun dato globale", Foreground = System.Windows.Media.Brushes.Gray });
            return;
        }

        foreach ((GlobalBatomonStat stat, int index) in podium.Select((stat, index) => (stat, index)))
        {
            int rank = index + 1;
            var card = new Border
            {
                Width = 82,
                Height = rank == 1 ? 132 : 110,
                Margin = new Thickness(4, rank == 1 ? 0 : 22, 4, 0),
                Padding = new Thickness(6),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(rank switch
                {
                    1 => "#FCCA46",
                    2 => "#E8E3D6",
                    _ => "#D8E8DE"
                })),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Media.Imaging.BitmapImage? icon = homeIconRepository.Load(stat.SpeciesId, false);
            if (icon is not null)
            {
                content.Children.Add(new System.Windows.Controls.Image { Source = icon, Width = 42, Height = 42, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            }
            content.Children.Add(new TextBlock { Text = $"0{rank}", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Gray, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = stat.SpeciesId, Margin = new Thickness(0, 3, 0, 0), FontSize = 12, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233d4d")) });
            content.Children.Add(new TextBlock { Text = valueSelector(stat).ToString(), Margin = new Thickness(0, 3, 0, 0), FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233d4d")) });
            card.Child = content;
            panel.Children.Add(card);
        }
    }

    private void PopulateHomeStats(IReadOnlyList<CompletedRun> runs)
    {
        var played = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var wins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var comboCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        CompletedRun? bestTeam = null;

        foreach (CompletedRun run in runs)
        {
            string[] species = ParseSpecies(run.TeamJson).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string speciesId in species)
            {
                played[speciesId] = played.GetValueOrDefault(speciesId) + 1;
                if (IsVictory(run))
                {
                    wins[speciesId] = wins.GetValueOrDefault(speciesId) + 1;
                }
            }

            if (IsVictory(run))
            {
                foreach (string[] pair in species.SelectMany((first, index) => species.Skip(index + 1).Select(second => new[] { first, second })))
                {
                    string key = string.Join(" + ", pair.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                    comboCounts[key] = comboCounts.GetValueOrDefault(key) + 1;
                }
            }

            if ((run.Wins ?? 0) >= 10 && (bestTeam is null || (run.Wins ?? 0) > (bestTeam.Wins ?? 0)))
            {
                bestTeam = run;
            }
        }

        Dispatcher.Invoke(() =>
        {
            RenderPodium(TopPlayedPodium, played.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Take(3));
            RenderPodium(TopWinsPodium, wins.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Take(3));

            KeyValuePair<string, int>? bestCombo = comboCounts.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Select(item => (KeyValuePair<string, int>?)item).FirstOrDefault();
            if (bestCombo.HasValue)
            {
                BestComboText.Text = bestCombo.Value.Key;
                BestComboDetailText.Text = $"Presente insieme in {bestCombo.Value.Value} run vinte.";
            }
            else
            {
                BestComboText.Text = "Dati insufficienti";
                BestComboDetailText.Text = "Servono almeno due Batomon nella stessa run vinta.";
            }

            if (bestTeam is not null)
            {
                BestTeamText.Text = string.Join("  ·  ", ParseSpecies(bestTeam.TeamJson));
                BestTeamDetailText.Text = $"Run {bestTeam.RunId} · {bestTeam.Wins} vittorie · {bestTeam.Outcome ?? "completata"}";
            }
            else
            {
                BestTeamText.Text = "Nessuna squadra ha ancora raggiunto 10 vittorie.";
                BestTeamDetailText.Text = "Continua a giocare per costruire il primo record.";
            }
        });
    }

    private static void RenderPodium(StackPanel panel, IEnumerable<KeyValuePair<string, int>> entries)
    {
        panel.Children.Clear();
        KeyValuePair<string, int>[] podium = entries.ToArray();
        if (podium.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Nessun dato", Foreground = System.Windows.Media.Brushes.Gray });
            return;
        }

        foreach ((KeyValuePair<string, int> entry, int index) in podium.Select((entry, index) => (entry, index)))
        {
            int rank = index + 1;
            var card = new Border
            {
                Width = 82,
                Height = rank == 1 ? 118 : 96,
                Margin = new Thickness(4, rank == 1 ? 0 : 22, 4, 0),
                Padding = new Thickness(8),
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(rank switch
                {
                    1 => "#FCCA46",
                    2 => "#E8E3D6",
                    _ => "#D8E8DE"
                })),
                CornerRadius = new CornerRadius(8),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock { Text = $"0{rank}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Gray, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = entry.Key, Margin = new Thickness(0, 6, 0, 0), FontSize = 13, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233d4d")) });
            content.Children.Add(new TextBlock { Text = entry.Value.ToString(), Margin = new Thickness(0, 5, 0, 0), FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#233d4d")) });
            card.Child = content;
            panel.Children.Add(card);
        }
    }

    private static string[] ParseSpecies(string teamJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(teamJson);
            return document.RootElement.ValueKind != JsonValueKind.Array
                ? Array.Empty<string>()
                : document.RootElement.EnumerateArray()
                    .Where(unit => unit.ValueKind == JsonValueKind.Object && unit.TryGetProperty("species_id", out _))
                    .Select(unit => unit.GetProperty("species_id").GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsVictory(CompletedRun run) =>
        (run.Wins ?? 0) >= 10 || string.Equals(run.Outcome, "won", StringComparison.OrdinalIgnoreCase);

    private async Task SyncOpponentCacheAsync(string accessToken)
    {
        try
        {
            OpponentCacheImportResult imported = await opponentCacheImporter.ImportAsync(new GodotSaveLocator().OpponentCacheDirectory);
            await remoteSyncClient.SyncOpponentCacheAsync(imported, accessToken);
        }
        catch (HttpRequestException)
        {
            // The local cache remains available when the API is offline.
        }
        catch (TaskCanceledException)
        {
            // A network timeout must not stop the desktop tracker.
        }
        catch (IOException)
        {
            // The game may be updating the cache while the tracker starts.
        }
        catch (System.Text.Json.JsonException)
        {
            // A malformed dataset is reported by the local analytics screen.
        }
    }

    private async Task SyncLiveRunAsync(string accessToken)
    {
        try
        {
            AccountFiles? account = new GodotSaveLocator().FindActiveAccount();
            if (account?.RunSavePath is null)
            {
                return;
            }

            string json = await File.ReadAllTextAsync(account.RunSavePath);
            RunState state = new RunSaveParser().Parse(json);
            await remoteSyncClient.SyncLiveRunAsync(state, accessToken);
            Dispatcher.Invoke(() => StatusText.Text = "Scansione iniziale completata: run live sincronizzata.");
        }
        catch (HttpRequestException)
        {
            // The local tracker remains usable when the API is offline.
        }
        catch (TaskCanceledException)
        {
            // A network timeout must not stop the desktop tracker.
        }
        catch (IOException)
        {
            // The game may be writing the save during startup.
        }
        catch (System.Text.Json.JsonException)
        {
            // An incomplete save is ignored until the next monitor refresh.
        }
    }

    private async Task ImportProfileHistoryAsync()
    {
        string? profilePath = new GodotSaveLocator().FindActiveAccount()?.ProfileSnapshotPath;
        if (profilePath is null)
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(profilePath);
            IReadOnlyList<CompletedRun> completedRuns = new ProfileHistoryParser().Parse(json);
            await store.ImportCompletedRunsAsync(completedRuns);
            if (authSessionStore.Load() is AuthSessionData session && !string.IsNullOrWhiteSpace(session.AccessToken))
            {
                try
                {
                    await remoteSyncClient.SyncCompletedRunsAsync(completedRuns, session.AccessToken);
                }
                catch (HttpRequestException)
                {
                    // Offline mode is expected: local persistence remains authoritative until the next startup.
                }
                catch (TaskCanceledException)
                {
                    // Offline mode is expected: local persistence remains authoritative until the next startup.
                }
            }
        }
        catch (IOException exception)
        {
            StatusText.Text = $"Storico profilo non disponibile: {exception.Message}";
        }
        catch (System.Text.Json.JsonException exception)
        {
            StatusText.Text = $"Storico profilo non valido: {exception.Message}";
        }
    }


    private void RegisterHotKeys(object? sender, EventArgs args)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(HandleHotKey);
        RegisterHotKey(handle, ToggleOverlayHotKeyId, ModControl | ModShift, VkO);
        RegisterHotKey(handle, ToggleInteractionHotKeyId, ModControl | ModShift, VkL);
    }

    private void UnregisterHotKeys()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, ToggleOverlayHotKeyId);
        UnregisterHotKey(handle, ToggleInteractionHotKeyId);
        overlay.Close();
    }

    private IntPtr HandleHotKey(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey)
        {
            int hotKeyId = wParam.ToInt32();
            if (hotKeyId == ToggleOverlayHotKeyId)
            {
                overlay.ToggleVisibility();
                handled = true;
            }
            else if (hotKeyId == ToggleInteractionHotKeyId)
            {
                overlay.ToggleInteractive();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
