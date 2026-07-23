using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;

namespace CodexPulse
{
    public sealed class MainWindow : Window, IDisposable
    {
        [Flags]
        private enum WindowResizeEdge
        {
            None = 0,
            Left = 1,
            Top = 2,
            Right = 4,
            Bottom = 8
        }

        private sealed class PulseMenuColorTable : Forms.ProfessionalColorTable
        {
            private static readonly DrawingColor MenuSurface = DrawingColor.FromArgb(252, 19, 24, 45);
            private static readonly DrawingColor MenuHover = DrawingColor.FromArgb(255, 48, 57, 91);
            private static readonly DrawingColor MenuAccent = DrawingColor.FromArgb(255, 92, 101, 157);

            public override DrawingColor ToolStripDropDownBackground { get { return MenuSurface; } }
            public override DrawingColor MenuBorder { get { return MenuAccent; } }
            public override DrawingColor MenuItemBorder { get { return DrawingColor.FromArgb(255, 74, 211, 190); } }
            public override DrawingColor MenuItemSelected { get { return MenuHover; } }
            public override DrawingColor MenuItemSelectedGradientBegin { get { return MenuHover; } }
            public override DrawingColor MenuItemSelectedGradientEnd { get { return MenuHover; } }
            public override DrawingColor ImageMarginGradientBegin { get { return MenuSurface; } }
            public override DrawingColor ImageMarginGradientMiddle { get { return MenuSurface; } }
            public override DrawingColor ImageMarginGradientEnd { get { return MenuSurface; } }
            public override DrawingColor SeparatorDark { get { return DrawingColor.FromArgb(255, 75, 82, 119); } }
            public override DrawingColor SeparatorLight { get { return DrawingColor.FromArgb(255, 75, 82, 119); } }
            public override DrawingColor CheckBackground { get { return MenuHover; } }
            public override DrawingColor CheckSelectedBackground { get { return MenuHover; } }
            public override DrawingColor CheckPressedBackground { get { return MenuHover; } }
        }

        private sealed class PulseMenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            public PulseMenuRenderer() : base(new PulseMenuColorTable()) { }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = DrawingColor.White;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
            {
                System.Drawing.Rectangle area = e.ImageRectangle;
                using (System.Drawing.Pen pen = new System.Drawing.Pen(DrawingColor.White, 2f))
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new System.Drawing.Point(area.Left + 2, area.Top + area.Height / 2),
                        new System.Drawing.Point(area.Left + area.Width / 2 - 1, area.Bottom - 3),
                        new System.Drawing.Point(area.Right - 2, area.Top + 3)
                    });
                }
            }
        }

        private const double ResizeHandleThickness = 9;
        private readonly SettingsStore store;
        private readonly QuotaApiClient apiClient;
        private readonly DispatcherTimer refreshTimer;
        private readonly Forms.NotifyIcon trayIcon;
        private Forms.ToolStripMenuItem trayPinItem;
        private TextBlock quotaValue;
        private TextBlock quotaDetail;
        private TextBlock cardValue;
        private TextBlock cardDetail;
        private Grid quotaProgressHost;
        private Border quotaProgressFill;
        private TextBlock statusText;
        private TextBlock footerText;
        private Button pinButton;
        private Button accountButton;
        private Popup accountPopup;
        private TextBlock accountMenuNotice;
        private AppSettings settings;
        private QuotaSnapshot snapshot;
        private bool refreshing;
        private long refreshRequestVersion;
        private string refreshingAccountId;
        private bool loginPolling;
        private DateTime loginPollingEndsAt;
        private string pendingLoginAccountId;
        private WindowResizeEdge activeResizeEdge;
        private Point resizeStartPoint;
        private double resizeStartLeft;
        private double resizeStartTop;
        private double resizeStartWidth;
        private double resizeStartHeight;
        private bool allowClose;
        private bool disposed;

        public MainWindow(SettingsStore settingsStore, QuotaApiClient client, bool forceMinimized)
        {
            store = settingsStore;
            apiClient = client;
            settings = store.LoadSettings();
            AccountProfile initialAccount = settings.GetActiveAccount();
            snapshot = initialAccount == null ? QuotaSnapshot.Empty() : store.LoadSnapshot(initialAccount.Id);
            pendingLoginAccountId = string.Empty;
            refreshingAccountId = string.Empty;

            Title = "Codex Pulse";
            Width = Math.Max(560, settings.WindowWidth);
            Height = Math.Max(320, settings.WindowHeight);
            MinWidth = 560;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = Theme.PrimaryText;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;
            ShowInTaskbar = true;
            Topmost = settings.AlwaysOnTop;
            PreviewMouseLeftButtonDown += BeginWindowResize;
            PreviewMouseMove += ResizeWindowOrUpdateCursor;
            PreviewMouseLeftButtonUp += EndWindowResize;
            SizeChanged += delegate { UpdateResponsiveLayout(); };

            Border shell = new Border
            {
                CornerRadius = new CornerRadius(26),
                BorderBrush = Theme.BrushFrom("#8A9CA5C8"),
                BorderThickness = new Thickness(1),
                Background = Theme.WindowGlass(),
                SnapsToDevicePixels = true
            };
            shell.SizeChanged += delegate
            {
                if (shell.ActualWidth > 0 && shell.ActualHeight > 0)
                {
                    shell.Clip = new RectangleGeometry(
                        new Rect(0, 0, shell.ActualWidth, shell.ActualHeight),
                        26,
                        26);
                }
            };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });

            Grid titleBar = BuildTitleBar(out statusText, out accountButton, out pinButton);
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            Grid dashboard = new Grid { Margin = new Thickness(24, 10, 24, 16) };
            dashboard.Children.Add(BuildQuotaDashboard(
                out quotaValue,
                out quotaDetail,
                out cardDetail,
                out cardValue,
                out quotaProgressHost,
                out quotaProgressFill));
            quotaProgressHost.SizeChanged += delegate
            {
                UpdateQuotaProgress(snapshot == null || !snapshot.IsAvailable ? 0 : snapshot.RemainingPercent);
            };
            Grid.SetRow(dashboard, 1);
            root.Children.Add(dashboard);

            Border footer = new Border
            {
                BorderBrush = Theme.BrushFrom("#3C9CA5C8"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(22, 0, 22, 0),
                Background = Theme.BrushFrom("#2A0A1025")
            };
            footerText = Theme.Text("最后更新：--", 12, Theme.MutedText, FontWeights.Normal);
            footerText.HorizontalAlignment = HorizontalAlignment.Center;
            footer.Child = footerText;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            shell.Child = root;
            Content = shell;
            trayIcon = BuildTrayIcon();
            UpdateSnapshot(snapshot, snapshot.IsAvailable ? "缓存数据" : "等待连接");

            refreshTimer = new DispatcherTimer(DispatcherPriority.Background);
            refreshTimer.Tick += delegate { RefreshNow(); };
            ApplyRefreshInterval();
            ApplyAlwaysOnTop(settings.AlwaysOnTop, false);

            Loaded += delegate
            {
                UpdateResponsiveLayout();
                refreshTimer.Start();
                RefreshNow();
                if (forceMinimized || settings.StartMinimized)
                {
                    HideToTray();
                }
            };
            Closing += WindowClosing;
        }

        private Grid BuildTitleBar(out TextBlock liveStatus, out Button connectButton, out Button pinToggle)
        {
            Grid bar = new Grid { Margin = new Thickness(22, 0, 12, 0), Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodexPulse.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    Image icon = new Image
                    {
                        Source = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute)),
                        Width = 34,
                        Height = 34,
                        Margin = new Thickness(0, 0, 11, 0)
                    };
                    brand.Children.Add(icon);
                }
                catch
                {
                    TextBlock mark = Theme.Text("◌", 28, Theme.Cyan, FontWeights.Bold);
                    mark.Margin = new Thickness(0, 0, 11, 0);
                    brand.Children.Add(mark);
                }
            }
            brand.Children.Add(Theme.Text("Codex Pulse", 21, Theme.PrimaryText, FontWeights.SemiBold));
            Grid.SetColumn(brand, 0);
            bar.Children.Add(brand);

            liveStatus = Theme.Text("● 正在连接", 12, Theme.Emerald, FontWeights.Normal);
            liveStatus.Margin = new Thickness(8, 0, 12, 0);
            Grid.SetColumn(liveStatus, 1);
            bar.Children.Add(liveStatus);

            connectButton = Theme.CompactButton("添加账号", "添加、切换或管理 ChatGPT/Codex 账号");
            connectButton.MaxWidth = 150;
            connectButton.Click += delegate { ShowAccountMenu(); };
            connectButton.MouseLeave += delegate { UpdateAccountButton(); };
            Grid.SetColumn(connectButton, 2);
            bar.Children.Add(connectButton);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button refresh = Theme.IconButton("↻", "立即刷新");
            pinToggle = Theme.CompactButton("置顶", "窗口始终置顶");
            pinToggle.MinWidth = 54;
            pinToggle.Margin = new Thickness(3, 0, 0, 0);
            Button configure = Theme.IconButton("⚙", "设置");
            Button minimize = Theme.IconButton("—", "最小化到托盘");
            Button close = Theme.IconButton("×", "关闭到托盘");
            refresh.Click += delegate { RefreshNow(); };
            pinToggle.Click += delegate { ApplyAlwaysOnTop(!settings.AlwaysOnTop, true); };
            pinToggle.MouseLeave += delegate { UpdatePinVisual(); };
            configure.Click += delegate { OpenSettings(); };
            minimize.Click += delegate { HideToTray(); };
            close.Click += delegate { HideToTray(); };
            actions.Children.Add(refresh);
            actions.Children.Add(pinToggle);
            actions.Children.Add(configure);
            actions.Children.Add(minimize);
            actions.Children.Add(close);
            Grid.SetColumn(actions, 3);
            bar.Children.Add(actions);
            return bar;
        }

        private static Border BuildQuotaDashboard(
            out TextBlock percentage,
            out TextBlock tokenValue,
            out TextBlock resetTimeValue,
            out TextBlock resetCountValue,
            out Grid progressHost,
            out Border progressFill)
        {
            Border panel = new Border
            {
                Background = Theme.BrushFrom("#45141933"),
                BorderBrush = Theme.BrushFrom("#668B84C5"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(22, 13, 22, 12)
            };
            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = Theme.Text("Codex 额度", 18, Theme.PrimaryText, FontWeights.SemiBold);
            heading.Children.Add(title);
            percentage = Theme.Text("--", 42, Theme.Emerald, FontWeights.Bold);
            percentage.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(percentage, 1);
            heading.Children.Add(percentage);
            Grid.SetRow(heading, 0);
            content.Children.Add(heading);

            LinearGradientBrush progressBrush = new LinearGradientBrush();
            progressBrush.StartPoint = new Point(0, 0.5);
            progressBrush.EndPoint = new Point(1, 0.5);
            progressBrush.GradientStops.Add(new GradientStop(((SolidColorBrush)Theme.BrushFrom("#39E6AE")).Color, 0));
            progressBrush.GradientStops.Add(new GradientStop(((SolidColorBrush)Theme.BrushFrom("#3ACBFF")).Color, 1));

            Border track = new Border
            {
                Height = 16,
                Background = Theme.BrushFrom("#67272A51"),
                BorderBrush = Theme.BrushFrom("#7C7974C8"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 3, 0, 0)
            };
            progressHost = new Grid { ClipToBounds = true };
            progressFill = new Border
            {
                Width = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = progressBrush,
                CornerRadius = new CornerRadius(8)
            };
            progressHost.Children.Add(progressFill);
            track.Child = progressHost;
            Grid.SetRow(track, 1);
            content.Children.Add(track);

            Grid scale = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            string[] scaleLabels = { "0%", "25%", "50%", "75%", "100%" };
            for (int i = 0; i < scaleLabels.Length; i++)
            {
                scale.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock mark = Theme.Text(scaleLabels[i], 10, Theme.MutedText, FontWeights.Normal);
                mark.HorizontalAlignment = i == 0
                    ? HorizontalAlignment.Left
                    : (i == scaleLabels.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center);
                Grid.SetColumn(mark, i);
                scale.Children.Add(mark);
            }
            Grid.SetRow(scale, 2);
            content.Children.Add(scale);

            Grid metrics = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            metrics.ColumnDefinitions.Add(new ColumnDefinition());

            FrameworkElement tokens = BuildMetricColumn("近 7 日用量", Theme.Emerald, out tokenValue);
            FrameworkElement reset = BuildMetricColumn("下次重置", Theme.Violet, out resetTimeValue);
            FrameworkElement resets = BuildMetricColumn("可用重置卡", Theme.Cyan, out resetCountValue);
            Grid.SetColumn(tokens, 0);
            Grid.SetColumn(reset, 2);
            Grid.SetColumn(resets, 4);
            metrics.Children.Add(tokens);
            metrics.Children.Add(reset);
            metrics.Children.Add(resets);
            for (int column = 1; column <= 3; column += 2)
            {
                Border separator = new Border
                {
                    Width = 1,
                    Background = Theme.BrushFrom("#4A7B83AC"),
                    Margin = new Thickness(0, 1, 0, 1)
                };
                Grid.SetColumn(separator, column);
                metrics.Children.Add(separator);
            }
            Grid.SetRow(metrics, 3);
            content.Children.Add(metrics);

            panel.Child = content;
            return panel;
        }

        private static FrameworkElement BuildMetricColumn(string label, Brush accent, out TextBlock value)
        {
            StackPanel panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 7, 0)
            };
            StackPanel heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            TextBlock dot = Theme.Text("●", 9, accent, FontWeights.Normal);
            dot.Margin = new Thickness(0, 0, 6, 0);
            heading.Children.Add(dot);
            heading.Children.Add(Theme.Text(label, 11, Theme.MutedText, FontWeights.Normal));
            panel.Children.Add(heading);
            value = Theme.Text("--", 13, Theme.PrimaryText, FontWeights.SemiBold);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            value.Margin = new Thickness(0, 3, 0, 0);
            panel.Children.Add(value);
            return panel;
        }

        public void RefreshNow()
        {
            if (loginPolling && DateTime.Now > loginPollingEndsAt)
            {
                loginPolling = false;
                pendingLoginAccountId = string.Empty;
                ApplyRefreshInterval();
            }

            AccountProfile activeAccount = settings.GetActiveAccount();
            if (activeAccount == null)
            {
                snapshot = QuotaSnapshot.Empty();
                UpdateSnapshot(snapshot, "等待连接");
                return;
            }
            string requestAccountId = activeAccount.Id;
            if (refreshing
                && string.Equals(refreshingAccountId, requestAccountId, StringComparison.Ordinal))
            {
                return;
            }
            long requestVersion = ++refreshRequestVersion;
            AppSettings requestSettings = settings.Clone();

            refreshing = true;
            refreshingAccountId = requestAccountId;
            statusText.Text = "● 正在刷新";
            statusText.Foreground = Theme.Cyan;
            footerText.Text = "正在刷新“" + ShortAccountLabel(activeAccount.Label) + "”的额度，请稍候…";
            RefreshOpenAccountMenu();

            Task<QuotaSnapshot> task;
            try
            {
                task = apiClient.FetchAsync(requestSettings);
            }
            catch (Exception exception)
            {
                RefreshFailed(exception, requestAccountId, requestVersion);
                return;
            }

            task.ContinueWith(completed =>
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (completed.IsCanceled)
                    {
                        RefreshFailed(
                            new InvalidOperationException("同步已取消。"),
                            requestAccountId,
                            requestVersion);
                    }
                    else if (completed.IsFaulted)
                    {
                        Exception error = completed.Exception == null ? null : completed.Exception.GetBaseException();
                        RefreshFailed(
                            error ?? new InvalidOperationException("同步失败。"),
                            requestAccountId,
                            requestVersion);
                    }
                    else
                    {
                        QuotaSnapshot result = completed.Result;
                        store.SaveSnapshot(requestAccountId, result);
                        UpdateAccountProfile(requestAccountId, result);
                        if (!IsCurrentRefresh(requestAccountId, requestVersion))
                        {
                            return;
                        }
                        snapshot = result;
                        loginPolling = false;
                        pendingLoginAccountId = string.Empty;
                        ApplyRefreshInterval();
                        UpdateSnapshot(snapshot, "实时数据");
                        refreshing = false;
                        refreshingAccountId = string.Empty;
                        RefreshOpenAccountMenu();
                    }
                }));
            });
        }

        private bool IsCurrentRefresh(string accountId, long requestVersion)
        {
            return requestVersion == refreshRequestVersion
                && string.Equals(refreshingAccountId, accountId, StringComparison.Ordinal)
                && string.Equals(settings.ActiveAccountId, accountId, StringComparison.Ordinal);
        }

        private void RefreshFailed(Exception error, string accountId, long requestVersion)
        {
            if (!IsCurrentRefresh(accountId, requestVersion))
            {
                return;
            }
            refreshing = false;
            refreshingAccountId = string.Empty;
            RefreshOpenAccountMenu();
            CodexLoginRequiredException loginError = error as CodexLoginRequiredException;
            if (loginError != null)
            {
                statusText.Text = loginPolling ? "● 等待登录" : "● 未连接账号";
                statusText.Foreground = Theme.Warning;
                footerText.Text = loginPolling ? "请在浏览器完成登录，应用将自动刷新" : "请添加或重新登录 ChatGPT/Codex 账号";
                UpdateAccountButton();
                return;
            }
            statusText.Text = "● 使用缓存";
            statusText.Foreground = Theme.Warning;
            footerText.Text = "同步失败：" + ShortMessage(error == null ? "未知错误" : error.Message);
        }

        private void UpdateSnapshot(QuotaSnapshot value, string source)
        {
            if (!value.IsAvailable)
            {
                quotaValue.Text = "--";
                quotaDetail.Text = "--";
                cardValue.Text = "--";
                cardDetail.Text = "--";
                footerText.Text = settings.GetActiveAccount() == null
                    ? "尚未添加 ChatGPT/Codex 账号"
                    : "当前账号尚未完成登录";
            }
            else
            {
                quotaValue.Text = string.Format("{0}%", value.RemainingPercent);
                if (value.IsLiveAccount)
                {
                    string tokens = value.HasWeeklyTokenData
                        ? string.Format("{0:N0} tokens", value.WeeklyTokensUsed)
                        : "官方未提供";
                    string reset = value.ResetsAtEpochSeconds > 0
                        ? string.Format("{0:yyyy-MM-dd HH:mm}", TimeUtil.FromEpochSeconds(value.ResetsAtEpochSeconds))
                        : "官方未提供";
                    quotaDetail.Text = tokens;
                    cardDetail.Text = reset;
                    cardValue.Text = value.HasResetCardData
                        ? string.Format("{0} 次", value.ResetCardsAvailable)
                        : "官方未提供";
                }
                else
                {
                    quotaDetail.Text = string.Format("{0:N0} tokens", Math.Max(0L, value.WeeklyTokensUsed));
                    cardDetail.Text = value.ResetsAtEpochSeconds > 0
                        ? string.Format("{0:yyyy-MM-dd HH:mm}", TimeUtil.FromEpochSeconds(value.ResetsAtEpochSeconds))
                        : "--";
                }
                if (!value.IsLiveAccount)
                {
                    cardValue.Text = string.Format("{0} 次", value.ResetCardsAvailable);
                }
                string email = string.IsNullOrWhiteSpace(value.AccountEmail) ? string.Empty : value.AccountEmail + " · ";
                footerText.Text = string.Format("{0}最后更新：{1:HH:mm:ss}", email, TimeUtil.FromEpochMillis(value.UpdatedAtEpochMillis));
            }

            UpdateQuotaProgress(value.IsAvailable ? value.RemainingPercent : 0);

            statusText.Text = "● " + source;
            statusText.Foreground = source == "实时数据" ? Theme.Emerald : Theme.Violet;
            UpdateAccountButton();
            trayIcon.Text = ShortMessage(value.IsAvailable
                ? string.Format("Codex {0}% · 重置卡 {1} 次", value.RemainingPercent, value.ResetCardsAvailable)
                : "Codex Pulse · 未连接账号");
        }

        private void ConnectAccount()
        {
            if (refreshing)
            {
                ShowAccountSwitchBlocked();
                return;
            }
            AccountProfile account = settings.GetActiveAccount();
            if (account == null)
            {
                AddAccount();
                return;
            }
            string accountId = account.Id;
            settings.DemoMode = false;
            settings.SettingsVersion = 4;
            store.SaveSettings(settings);
            accountButton.Content = "打开浏览器";
            statusText.Text = "● 准备登录";
            statusText.Foreground = Theme.Cyan;

            Task<string> task = apiClient.BeginLoginAsync(accountId);
            task.ContinueWith(completed =>
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (completed.IsFaulted)
                    {
                        Exception error = completed.Exception == null ? null : completed.Exception.GetBaseException();
                        if (string.Equals(settings.ActiveAccountId, accountId, StringComparison.Ordinal))
                        {
                            MessageBox.Show(this, error == null ? "无法启动账号登录。" : error.Message, "Codex Pulse", MessageBoxButton.OK, MessageBoxImage.Warning);
                            statusText.Text = "● 登录失败";
                            statusText.Foreground = Theme.Warning;
                            UpdateAccountButton();
                        }
                        return;
                    }
                    if (!string.Equals(settings.ActiveAccountId, accountId, StringComparison.Ordinal))
                    {
                        return;
                    }
                    loginPolling = true;
                    pendingLoginAccountId = accountId;
                    loginPollingEndsAt = DateTime.Now.AddMinutes(3);
                    refreshTimer.Interval = TimeSpan.FromSeconds(5);
                    statusText.Text = "● 等待登录";
                    statusText.Foreground = Theme.Cyan;
                    accountButton.Content = "等待登录";
                    footerText.Text = "请在浏览器完成登录，应用将自动刷新";
                }));
            });
        }

        private void ShowAccountMenu()
        {
            if (accountPopup != null && accountPopup.IsOpen)
            {
                accountPopup.IsOpen = false;
                return;
            }

            accountPopup = new Popup
            {
                PlacementTarget = accountButton,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                StaysOpen = false,
                HorizontalOffset = Math.Min(0, accountButton.ActualWidth - 440),
                VerticalOffset = 6
            };
            accountPopup.Child = BuildAccountMenuPanel();
            accountPopup.Closed += delegate
            {
                accountMenuNotice = null;
                accountPopup = null;
            };
            accountPopup.IsOpen = true;
        }

        private Border BuildAccountMenuPanel()
        {
            Border panel = new Border
            {
                Width = 440,
                MaxHeight = 510,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(18),
                Background = Theme.CardGlass(false),
                BorderBrush = Theme.BrushFrom("#B58B7CFF"),
                BorderThickness = new Thickness(1.5),
                Effect = new DropShadowEffect
                {
                    Color = ((SolidColorBrush)Theme.BrushFrom("#C0000000")).Color,
                    BlurRadius = 24,
                    ShadowDepth = 8,
                    Opacity = 0.65
                }
            };
            StackPanel content = new StackPanel();

            TextBlock title = Theme.Text("账号", 12, Theme.MutedText, FontWeights.SemiBold);
            title.Margin = new Thickness(8, 2, 8, 8);
            content.Children.Add(title);

            StackPanel accounts = new StackPanel();
            if (settings.Accounts != null)
            {
                foreach (AccountProfile profile in settings.Accounts)
                {
                    AccountProfile account = profile;
                    bool selected = string.Equals(account.Id, settings.ActiveAccountId, StringComparison.Ordinal);
                    QuotaSnapshot accountSnapshot = selected ? snapshot : store.LoadSnapshot(account.Id);
                    bool loading = refreshing
                        && string.Equals(refreshingAccountId, account.Id, StringComparison.Ordinal);
                    accounts.Children.Add(BuildAccountRow(account, accountSnapshot, selected, loading));
                }
            }

            ScrollViewer accountScroller = new ScrollViewer
            {
                Content = accounts,
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            content.Children.Add(accountScroller);

            accountMenuNotice = Theme.Text(
                "额度刷新中，请稍后再切换账号",
                12,
                Theme.Warning,
                FontWeights.Normal);
            accountMenuNotice.HorizontalAlignment = HorizontalAlignment.Center;
            accountMenuNotice.Margin = new Thickness(8, 8, 8, 0);
            accountMenuNotice.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
            content.Children.Add(accountMenuNotice);

            Border separator = new Border
            {
                Height = 1,
                Background = Theme.BrushFrom("#547B83AC"),
                Margin = new Thickness(4, 11, 4, 9)
            };
            content.Children.Add(separator);

            Grid actions = new Grid();
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            Button add = AccountActionButton("＋  添加账号");
            add.Click += delegate { AddAccount(); };
            actions.Children.Add(add);

            Button login = AccountActionButton("↻  重新登录");
            login.IsEnabled = settings.GetActiveAccount() != null;
            login.Click += delegate { ConnectAccount(); };
            Grid.SetColumn(login, 1);
            actions.Children.Add(login);

            Button remove = AccountActionButton("−  移除账号");
            remove.Foreground = Theme.Warning;
            remove.IsEnabled = settings.GetActiveAccount() != null;
            remove.Click += delegate { RemoveActiveAccount(); };
            Grid.SetColumn(remove, 2);
            actions.Children.Add(remove);
            content.Children.Add(actions);

            panel.Child = content;
            return panel;
        }

        private Border BuildAccountRow(
            AccountProfile account,
            QuotaSnapshot accountSnapshot,
            bool selected,
            bool loading)
        {
            Brush restingBackground = loading
                ? Theme.BrushFrom("#583B3C71")
                : (selected ? Theme.BrushFrom("#3D2C6B67") : Theme.BrushFrom("#24191D3A"));
            Border row = new Border
            {
                Background = restingBackground,
                BorderBrush = loading
                    ? Theme.BrushFrom("#8F8B7CFF")
                    : (selected ? Theme.BrushFrom("#8139E6AE") : Theme.BrushFrom("#3E7B83AC")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12, 10, 12, loading ? 11 : 10),
                Margin = new Thickness(0, 0, 0, 7),
                Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrWhiteSpace(account.Email) ? "账号尚未完成登录" : account.Email
            };
            row.MouseEnter += delegate { row.Background = Theme.BrushFrom("#62434B7A"); };
            row.MouseLeave += delegate { row.Background = restingBackground; };
            row.MouseLeftButtonUp += delegate { SwitchAccount(account.Id); };

            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (loading)
            {
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock selection = Theme.Text(selected ? "✓" : string.Empty, 21, Theme.Emerald, FontWeights.SemiBold);
            selection.HorizontalAlignment = HorizontalAlignment.Center;
            heading.Children.Add(selection);

            Border avatar = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = selected ? Theme.BrushFrom("#5840D6BD") : Theme.BrushFrom("#685E50C8"),
                BorderBrush = selected ? Theme.Emerald : Theme.Violet,
                BorderThickness = new Thickness(1)
            };
            string label = account.Label;
            avatar.Child = Theme.Text(
                string.IsNullOrWhiteSpace(label) ? "?" : label.Substring(0, 1).ToUpperInvariant(),
                15,
                Theme.PrimaryText,
                FontWeights.SemiBold);
            ((TextBlock)avatar.Child).HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(avatar, 1);
            heading.Children.Add(avatar);

            TextBlock accountLabel = Theme.Text(ShortAccountLabel(account.Label), 14, Theme.PrimaryText, FontWeights.SemiBold);
            accountLabel.Margin = new Thickness(9, 0, 8, 0);
            Grid.SetColumn(accountLabel, 2);
            heading.Children.Add(accountLabel);

            string quotaText = accountSnapshot != null && accountSnapshot.IsAvailable
                ? accountSnapshot.RemainingPercent + "%"
                : "--";
            Border quotaPill = new Border
            {
                Background = Theme.BrushFrom("#3D39E6AE"),
                BorderBrush = Theme.BrushFrom("#7039E6AE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3)
            };
            quotaPill.Child = Theme.Text(quotaText, 13, Theme.Emerald, FontWeights.SemiBold);
            Grid.SetColumn(quotaPill, 3);
            heading.Children.Add(quotaPill);
            content.Children.Add(heading);

            if (loading)
            {
                StackPanel loadingPanel = new StackPanel { Margin = new Thickness(72, 8, 2, 0) };
                TextBlock loadingText = Theme.Text("正在刷新额度信息，请稍候…", 12, Theme.SecondaryText, FontWeights.Normal);
                loadingPanel.Children.Add(loadingText);

                Border progressTrack = new Border
                {
                    Height = 5,
                    Background = Theme.BrushFrom("#5530385E"),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 7, 0, 0),
                    ClipToBounds = true
                };
                LinearGradientBrush progressBrush = new LinearGradientBrush();
                progressBrush.StartPoint = new Point(0, 0.5);
                progressBrush.EndPoint = new Point(1, 0.5);
                progressBrush.GradientStops.Add(new GradientStop(((SolidColorBrush)Theme.Cyan).Color, 0));
                progressBrush.GradientStops.Add(new GradientStop(((SolidColorBrush)Theme.Violet).Color, 0.72));
                progressBrush.GradientStops.Add(new GradientStop(((SolidColorBrush)Theme.Emerald).Color, 1));
                Border progress = new Border
                {
                    Width = 90,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = progressBrush,
                    CornerRadius = new CornerRadius(3)
                };
                progressTrack.Child = progress;
                loadingPanel.Children.Add(progressTrack);
                progress.BeginAnimation(
                    FrameworkElement.WidthProperty,
                    new DoubleAnimation
                    {
                        From = 55,
                        To = 310,
                        Duration = TimeSpan.FromSeconds(1.25),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                Grid.SetRow(loadingPanel, 1);
                content.Children.Add(loadingPanel);
            }

            row.Child = content;
            return row;
        }

        private static Button AccountActionButton(string text)
        {
            Button button = Theme.CompactButton(text, text);
            button.MinWidth = 0;
            button.Height = 34;
            button.Margin = new Thickness(2, 0, 2, 0);
            button.Padding = new Thickness(6, 0, 6, 0);
            button.BorderBrush = Theme.BrushFrom("#357B83AC");
            return button;
        }

        private void RefreshOpenAccountMenu()
        {
            if (accountPopup == null || !accountPopup.IsOpen)
            {
                return;
            }
            accountPopup.Child = BuildAccountMenuPanel();
        }

        private void ShowAccountSwitchBlocked()
        {
            const string message = "额度刷新中，请稍后再切换账号";
            statusText.Text = "● 额度刷新中";
            statusText.Foreground = Theme.Cyan;
            footerText.Text = message;
            if (accountMenuNotice != null)
            {
                accountMenuNotice.Text = message;
                accountMenuNotice.Visibility = Visibility.Visible;
            }
        }

        private void AddAccount()
        {
            if (refreshing)
            {
                ShowAccountSwitchBlocked();
                return;
            }
            AccountProfile account = new AccountProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = "新账号 " + (settings.Accounts.Count + 1),
                CreatedAtEpochMillis = TimeUtil.NowEpochMillis(),
                LastUsedAtEpochMillis = TimeUtil.NowEpochMillis()
            };
            settings.Accounts.Add(account);
            settings.ActiveAccountId = account.Id;
            settings.SettingsVersion = 4;
            store.SaveSettings(settings);
            snapshot = QuotaSnapshot.Empty();
            snapshot.AccountId = account.Id;
            UpdateSnapshot(snapshot, "等待登录");
            ConnectAccount();
        }

        private void SwitchAccount(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }
            if (string.Equals(settings.ActiveAccountId, accountId, StringComparison.Ordinal))
            {
                return;
            }
            if (refreshing)
            {
                ShowAccountSwitchBlocked();
                return;
            }
            AccountProfile account = settings.Accounts.Find(delegate(AccountProfile value)
            {
                return value != null && string.Equals(value.Id, accountId, StringComparison.Ordinal);
            });
            if (account == null)
            {
                return;
            }
            if (accountPopup != null)
            {
                accountPopup.IsOpen = false;
            }
            settings.ActiveAccountId = account.Id;
            account.LastUsedAtEpochMillis = TimeUtil.NowEpochMillis();
            settings.SettingsVersion = 4;
            store.SaveSettings(settings);
            loginPolling = false;
            pendingLoginAccountId = string.Empty;
            ApplyRefreshInterval();
            snapshot = store.LoadSnapshot(account.Id);
            UpdateSnapshot(snapshot, snapshot.IsAvailable ? "缓存数据" : "等待连接");
            RefreshNow();
        }

        private void RemoveActiveAccount()
        {
            if (refreshing)
            {
                ShowAccountSwitchBlocked();
                return;
            }
            AccountProfile account = settings.GetActiveAccount();
            if (account == null)
            {
                return;
            }
            MessageBoxResult answer = MessageBox.Show(
                this,
                "确定移除“" + account.Label + "”吗？\n\n这会删除 Codex Pulse 为该账号保存的本地登录信息，不会注销或删除 ChatGPT 账号。",
                "移除账号",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            apiClient.RemoveAccount(account.Id);
            try
            {
                store.DeleteAccountData(account.Id);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "无法移除账号", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            settings.Accounts.RemoveAll(delegate(AccountProfile value)
            {
                return value != null && string.Equals(value.Id, account.Id, StringComparison.Ordinal);
            });
            settings.ActiveAccountId = settings.Accounts.Count == 0 ? string.Empty : settings.Accounts[0].Id;
            settings.SettingsVersion = 4;
            store.SaveSettings(settings);
            loginPolling = false;
            pendingLoginAccountId = string.Empty;
            snapshot = settings.GetActiveAccount() == null
                ? QuotaSnapshot.Empty()
                : store.LoadSnapshot(settings.ActiveAccountId);
            UpdateSnapshot(snapshot, snapshot.IsAvailable ? "缓存数据" : "等待连接");
            if (settings.GetActiveAccount() != null)
            {
                RefreshNow();
            }
        }

        private void UpdateAccountProfile(string accountId, QuotaSnapshot value)
        {
            AccountProfile account = settings.Accounts.Find(delegate(AccountProfile profile)
            {
                return profile != null && string.Equals(profile.Id, accountId, StringComparison.Ordinal);
            });
            if (account == null)
            {
                return;
            }
            account.Email = value.AccountEmail ?? string.Empty;
            account.PlanType = value.PlanType ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(account.Email))
            {
                account.DisplayName = account.Email;
            }
            account.LastUsedAtEpochMillis = TimeUtil.NowEpochMillis();
            settings.SettingsVersion = 4;
            store.SaveSettings(settings);
        }

        private static string ShortAccountLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "未连接账号";
            }
            return value.Length <= 26 ? value : value.Substring(0, 23) + "…";
        }

        private static string ShortAccountButtonLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "未连接账号";
            }
            return value.Length <= 16 ? value : value.Substring(0, 13) + "…";
        }

        private void OpenSettings()
        {
            SettingsWindow dialog = new SettingsWindow(settings) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                settings = dialog.Result;
                store.SaveSettings(settings);
                try
                {
                    store.SetAutoStart(settings.AutoStart, Assembly.GetExecutingAssembly().Location);
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "开机启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                ApplyRefreshInterval();
                ApplyAlwaysOnTop(settings.AlwaysOnTop, false);
                if (!snapshot.IsLiveAccount)
                {
                    snapshot = QuotaSnapshot.Empty();
                    AccountProfile active = settings.GetActiveAccount();
                    if (active != null)
                    {
                        snapshot.AccountId = active.Id;
                    }
                    UpdateSnapshot(snapshot, "等待连接");
                }
                RefreshNow();
            }
        }

        private void ApplyRefreshInterval()
        {
            if (!loginPolling)
            {
                refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, settings.RefreshSeconds));
            }
        }

        private void ApplyAlwaysOnTop(bool enabled, bool persist)
        {
            settings.AlwaysOnTop = enabled;
            Topmost = enabled;
            if (persist)
            {
                settings.SettingsVersion = 4;
                store.SaveSettings(settings);
            }
            UpdatePinVisual();
        }

        private void UpdatePinVisual()
        {
            if (pinButton != null)
            {
                pinButton.Content = settings.AlwaysOnTop ? "已置顶" : "置顶";
                pinButton.Foreground = settings.AlwaysOnTop ? Brushes.White : Theme.SecondaryText;
                pinButton.Background = settings.AlwaysOnTop ? Theme.Violet : Brushes.Transparent;
                pinButton.BorderBrush = settings.AlwaysOnTop ? Theme.Violet : Brushes.Transparent;
            }
            if (trayPinItem != null)
            {
                trayPinItem.Checked = settings.AlwaysOnTop;
            }
        }

        private void UpdateResponsiveLayout()
        {
            if (statusText != null)
            {
                statusText.Visibility = ActualWidth > 760 ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateQuotaProgress(snapshot == null || !snapshot.IsAvailable ? 0 : snapshot.RemainingPercent);
        }

        private void UpdateQuotaProgress(int percentage)
        {
            if (quotaProgressHost == null || quotaProgressFill == null)
            {
                return;
            }
            int clamped = Math.Max(0, Math.Min(100, percentage));
            quotaProgressFill.Width = Math.Max(0, quotaProgressHost.ActualWidth * clamped / 100.0);
        }

        private void BeginWindowResize(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || WindowState != WindowState.Normal)
            {
                return;
            }
            WindowResizeEdge edge = HitTestResizeEdge(e.GetPosition(this));
            if (edge == WindowResizeEdge.None)
            {
                return;
            }

            activeResizeEdge = edge;
            resizeStartPoint = ToScreenDip(e.GetPosition(this));
            resizeStartLeft = Left;
            resizeStartTop = Top;
            resizeStartWidth = ActualWidth;
            resizeStartHeight = ActualHeight;
            CaptureMouse();
            e.Handled = true;
        }

        private void ResizeWindowOrUpdateCursor(object sender, MouseEventArgs e)
        {
            if (activeResizeEdge == WindowResizeEdge.None)
            {
                Cursor = CursorForEdge(HitTestResizeEdge(e.GetPosition(this)));
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishWindowResize();
                return;
            }

            Point current = ToScreenDip(e.GetPosition(this));
            double deltaX = current.X - resizeStartPoint.X;
            double deltaY = current.Y - resizeStartPoint.Y;
            double width = resizeStartWidth;
            double height = resizeStartHeight;
            double left = resizeStartLeft;
            double top = resizeStartTop;

            if ((activeResizeEdge & WindowResizeEdge.Right) != 0)
            {
                width = Math.Max(MinWidth, resizeStartWidth + deltaX);
            }
            if ((activeResizeEdge & WindowResizeEdge.Bottom) != 0)
            {
                height = Math.Max(MinHeight, resizeStartHeight + deltaY);
            }
            if ((activeResizeEdge & WindowResizeEdge.Left) != 0)
            {
                width = Math.Max(MinWidth, resizeStartWidth - deltaX);
                left = resizeStartLeft + resizeStartWidth - width;
            }
            if ((activeResizeEdge & WindowResizeEdge.Top) != 0)
            {
                height = Math.Max(MinHeight, resizeStartHeight - deltaY);
                top = resizeStartTop + resizeStartHeight - height;
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
            e.Handled = true;
        }

        private void EndWindowResize(object sender, MouseButtonEventArgs e)
        {
            if (activeResizeEdge == WindowResizeEdge.None)
            {
                return;
            }
            FinishWindowResize();
            e.Handled = true;
        }

        private void FinishWindowResize()
        {
            activeResizeEdge = WindowResizeEdge.None;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            Cursor = Cursors.Arrow;
            settings.WindowWidth = Math.Max(MinWidth, ActualWidth);
            settings.WindowHeight = Math.Max(MinHeight, ActualHeight);
            settings.SettingsVersion = 4;
            try
            {
                store.SaveSettings(settings);
            }
            catch { }
        }

        private WindowResizeEdge HitTestResizeEdge(Point point)
        {
            WindowResizeEdge edge = WindowResizeEdge.None;
            if (point.X <= ResizeHandleThickness)
            {
                edge |= WindowResizeEdge.Left;
            }
            else if (point.X >= ActualWidth - ResizeHandleThickness)
            {
                edge |= WindowResizeEdge.Right;
            }
            if (point.Y <= ResizeHandleThickness)
            {
                edge |= WindowResizeEdge.Top;
            }
            else if (point.Y >= ActualHeight - ResizeHandleThickness)
            {
                edge |= WindowResizeEdge.Bottom;
            }
            return edge;
        }

        private Point ToScreenDip(Point localPoint)
        {
            Point devicePoint = PointToScreen(localPoint);
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice.Transform(devicePoint);
            }
            return devicePoint;
        }

        private static Cursor CursorForEdge(WindowResizeEdge edge)
        {
            if (edge == (WindowResizeEdge.Left | WindowResizeEdge.Top)
                || edge == (WindowResizeEdge.Right | WindowResizeEdge.Bottom))
            {
                return Cursors.SizeNWSE;
            }
            if (edge == (WindowResizeEdge.Right | WindowResizeEdge.Top)
                || edge == (WindowResizeEdge.Left | WindowResizeEdge.Bottom))
            {
                return Cursors.SizeNESW;
            }
            if ((edge & (WindowResizeEdge.Left | WindowResizeEdge.Right)) != 0)
            {
                return Cursors.SizeWE;
            }
            if ((edge & (WindowResizeEdge.Top | WindowResizeEdge.Bottom)) != 0)
            {
                return Cursors.SizeNS;
            }
            return Cursors.Arrow;
        }

        private void UpdateAccountButton()
        {
            if (accountButton == null)
            {
                return;
            }
            bool connected = snapshot != null && snapshot.IsLiveAccount;
            AccountProfile active = settings.GetActiveAccount();
            if (active == null)
            {
                accountButton.Content = "添加账号";
            }
            else if (loginPolling && string.Equals(pendingLoginAccountId, active.Id, StringComparison.Ordinal))
            {
                accountButton.Content = "等待登录 ▾";
            }
            else
            {
                accountButton.Content = ShortAccountButtonLabel(active.Label) + " ▾";
            }
            accountButton.ToolTip = active == null
                ? "添加 ChatGPT/Codex 账号"
                : "当前账号：" + active.Label + "\n点击切换或管理账号";
            accountButton.Foreground = connected ? Theme.Emerald : Theme.SecondaryText;
            accountButton.Background = connected ? Theme.BrushFrom("#4139E6AE") : Brushes.Transparent;
            accountButton.BorderBrush = connected ? Theme.BrushFrom("#7339E6AE") : Theme.Border;
        }

        private Forms.NotifyIcon BuildTrayIcon()
        {
            Forms.NotifyIcon icon = new Forms.NotifyIcon();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CodexPulse.ico");
            icon.Icon = File.Exists(iconPath) ? new DrawingIcon(iconPath) : DrawingSystemIcons.Application;
            icon.Text = "Codex Pulse";
            icon.Visible = true;
            icon.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowFromTray)); };

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip
            {
                BackColor = DrawingColor.FromArgb(252, 19, 24, 45),
                ForeColor = DrawingColor.White,
                Renderer = new PulseMenuRenderer(),
                Font = new System.Drawing.Font("Microsoft YaHei UI", 10f, System.Drawing.FontStyle.Regular),
                Padding = new Forms.Padding(6),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                DropShadowEnabled = true
            };
            Forms.ToolStripMenuItem show = new Forms.ToolStripMenuItem("显示 / 隐藏");
            Forms.ToolStripMenuItem refresh = new Forms.ToolStripMenuItem("立即刷新");
            Forms.ToolStripMenuItem connect = new Forms.ToolStripMenuItem("添加 / 切换账号");
            trayPinItem = new Forms.ToolStripMenuItem("窗口始终置顶");
            trayPinItem.CheckOnClick = false;
            Forms.ToolStripMenuItem configure = new Forms.ToolStripMenuItem("设置");
            Forms.ToolStripMenuItem exit = new Forms.ToolStripMenuItem("退出");
            show.Click += delegate { Dispatcher.BeginInvoke(new Action(ToggleVisibility)); };
            refresh.Click += delegate { Dispatcher.BeginInvoke(new Action(RefreshNow)); };
            connect.Click += delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowFromTray(); ShowAccountMenu(); })); };
            trayPinItem.Click += delegate { Dispatcher.BeginInvoke(new Action(delegate { ApplyAlwaysOnTop(!settings.AlwaysOnTop, true); })); };
            configure.Click += delegate { Dispatcher.BeginInvoke(new Action(delegate { ShowFromTray(); OpenSettings(); })); };
            exit.Click += delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); };
            menu.Items.Add(show);
            menu.Items.Add(refresh);
            menu.Items.Add(connect);
            menu.Items.Add(trayPinItem);
            menu.Items.Add(configure);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exit);
            foreach (Forms.ToolStripItem item in menu.Items)
            {
                item.ForeColor = DrawingColor.White;
                if (item is Forms.ToolStripMenuItem)
                {
                    item.Padding = new Forms.Padding(10, 5, 20, 5);
                }
            }
            icon.ContextMenuStrip = menu;
            return icon;
        }

        private void ToggleVisibility()
        {
            if (IsVisible) HideToTray(); else ShowFromTray();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void HideToTray()
        {
            Hide();
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            if (!allowClose)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void ExitApplication()
        {
            allowClose = true;
            Close();
            Application.Current.Shutdown();
        }

        private static string ShortMessage(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Codex Pulse";
            return value.Length <= 60 ? value : value.Substring(0, 60);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            refreshTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            apiClient.Dispose();
        }
    }
}
