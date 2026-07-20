using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPulse
{
    public sealed class SettingsWindow : Window
    {
        private readonly CheckBox autoStart;
        private readonly CheckBox startMinimized;
        private readonly CheckBox alwaysOnTop;
        private readonly Dictionary<int, Button> refreshButtons = new Dictionary<int, Button>();
        private readonly double savedWindowWidth;
        private readonly double savedWindowHeight;
        private int selectedRefreshSeconds;

        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings initial)
        {
            savedWindowWidth = initial.WindowWidth;
            savedWindowHeight = initial.WindowHeight;
            selectedRefreshSeconds = IsKnownInterval(initial.RefreshSeconds) ? initial.RefreshSeconds : 60;

            Title = "Codex Pulse 设置";
            Width = 500;
            Height = 390;
            MinWidth = 500;
            MinHeight = 390;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Foreground = Theme.PrimaryText;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            UseLayoutRounding = true;

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
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });

            Grid titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            Border panel = new Border
            {
                Margin = new Thickness(24, 8, 24, 14),
                Padding = new Thickness(22, 18, 22, 18),
                Background = Theme.BrushFrom("#52141933"),
                BorderBrush = Theme.BrushFrom("#668B84C5"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20)
            };
            StackPanel form = new StackPanel();

            TextBlock refreshLabel = Theme.Text("刷新周期", 13, Theme.PrimaryText, FontWeights.SemiBold);
            refreshLabel.Margin = new Thickness(0, 0, 0, 10);
            form.Children.Add(refreshLabel);
            form.Children.Add(BuildRefreshSelector());

            Border separator = new Border
            {
                Height = 1,
                Background = Theme.BrushFrom("#467B83AC"),
                Margin = new Thickness(0, 18, 0, 12)
            };
            form.Children.Add(separator);

            TextBlock startupLabel = Theme.Text("启动选项", 13, Theme.PrimaryText, FontWeights.SemiBold);
            startupLabel.Margin = new Thickness(0, 0, 0, 4);
            form.Children.Add(startupLabel);
            autoStart = Toggle("开机自动启动", initial.AutoStart);
            startMinimized = Toggle("启动后最小化到托盘", initial.StartMinimized);
            alwaysOnTop = Toggle("窗口始终置顶", initial.AlwaysOnTop);
            form.Children.Add(autoStart);
            form.Children.Add(startMinimized);
            form.Children.Add(alwaysOnTop);

            panel.Child = form;
            Grid.SetRow(panel, 1);
            root.Children.Add(panel);

            Border actionBar = new Border
            {
                BorderBrush = Theme.BrushFrom("#3C9CA5C8"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = Theme.BrushFrom("#2A0A1025"),
                Padding = new Thickness(20, 0, 20, 0)
            };
            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Button cancel = ActionButton("取消", false);
            Button save = ActionButton("保存", true);
            cancel.Click += delegate { DialogResult = false; };
            save.Click += SaveClicked;
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            actionBar.Child = actions;
            Grid.SetRow(actionBar, 2);
            root.Children.Add(actionBar);

            shell.Child = root;
            Content = shell;
            UpdateRefreshButtons();
        }

        private Grid BuildTitleBar()
        {
            Grid bar = new Grid { Margin = new Thickness(24, 0, 12, 0), Background = Brushes.Transparent };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            };

            StackPanel heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border icon = new Border
            {
                Width = 30,
                Height = 30,
                Background = Theme.BrushFrom("#4A8B7CFF"),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 11, 0)
            };
            TextBlock gear = Theme.Text("⚙", 16, Brushes.White, FontWeights.Normal);
            gear.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Child = gear;
            heading.Children.Add(icon);
            heading.Children.Add(Theme.Text("设置", 21, Theme.PrimaryText, FontWeights.SemiBold));
            bar.Children.Add(heading);

            Button close = Theme.IconButton("×", "关闭设置");
            close.VerticalAlignment = VerticalAlignment.Center;
            close.Click += delegate { DialogResult = false; };
            Grid.SetColumn(close, 1);
            bar.Children.Add(close);
            return bar;
        }

        private Grid BuildRefreshSelector()
        {
            Grid selector = new Grid();
            int[] values = { 15, 30, 60, 300 };
            string[] labels = { "15 秒", "30 秒", "60 秒", "5 分钟" };
            for (int i = 0; i < values.Length; i++)
            {
                selector.ColumnDefinitions.Add(new ColumnDefinition());
                int value = values[i];
                Button button = Theme.CompactButton(labels[i], "每 " + labels[i] + "刷新一次");
                button.MinWidth = 0;
                button.Height = 36;
                button.Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Click += delegate
                {
                    selectedRefreshSeconds = value;
                    UpdateRefreshButtons();
                };
                button.MouseLeave += delegate { UpdateRefreshButtons(); };
                refreshButtons[value] = button;
                Grid.SetColumn(button, i);
                selector.Children.Add(button);
            }
            return selector;
        }

        private void UpdateRefreshButtons()
        {
            foreach (KeyValuePair<int, Button> item in refreshButtons)
            {
                bool selected = item.Key == selectedRefreshSeconds;
                item.Value.Background = selected ? Theme.Violet : Theme.Surface;
                item.Value.BorderBrush = selected ? Theme.Violet : Theme.Border;
                item.Value.Foreground = Brushes.White;
            }
        }

        private void SaveClicked(object sender, RoutedEventArgs e)
        {
            Result = new AppSettings
            {
                SettingsVersion = 3,
                DemoMode = false,
                Endpoint = string.Empty,
                AccessToken = string.Empty,
                RefreshSeconds = selectedRefreshSeconds,
                AutoStart = autoStart.IsChecked == true,
                StartMinimized = startMinimized.IsChecked == true,
                WidgetMode = false,
                AlwaysOnTop = alwaysOnTop.IsChecked == true,
                WindowWidth = savedWindowWidth,
                WindowHeight = savedWindowHeight
            };
            DialogResult = true;
        }

        private static CheckBox Toggle(string text, bool value)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = value,
                Foreground = Theme.PrimaryText,
                Margin = new Thickness(0, 6, 0, 6),
                Cursor = Cursors.Hand,
                Template = ToggleTemplate()
            };
        }

        private static ControlTemplate ToggleTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(CheckBox));
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory box = new FrameworkElementFactory(typeof(Border));
            box.Name = "CheckBoxBorder";
            box.SetValue(FrameworkElement.WidthProperty, 18.0);
            box.SetValue(FrameworkElement.HeightProperty, 18.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            box.SetValue(Border.BackgroundProperty, Theme.BrushFrom("#6A171C35"));
            box.SetValue(Border.BorderBrushProperty, Theme.Border);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));

            FrameworkElementFactory tick = new FrameworkElementFactory(typeof(TextBlock));
            tick.Name = "CheckMark";
            tick.SetValue(TextBlock.TextProperty, "✓");
            tick.SetValue(TextBlock.FontSizeProperty, 13.0);
            tick.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            tick.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            tick.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            tick.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(tick);
            panel.AppendChild(box);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Theme.PrimaryText);
            panel.AppendChild(content);
            template.VisualTree = panel;

            Trigger checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Theme.Violet, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Theme.Violet, "CheckBoxBorder"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            template.Triggers.Add(checkedTrigger);
            return template;
        }

        private static Button ActionButton(string text, bool primary)
        {
            Button button = Theme.CompactButton(text, text);
            button.Width = 92;
            button.Height = 38;
            button.Margin = new Thickness(8, 0, 0, 0);
            button.Background = primary ? Theme.Violet : Theme.Surface;
            button.Foreground = Brushes.White;
            button.BorderBrush = primary ? Theme.Violet : Theme.Border;
            button.MouseLeave += delegate
            {
                button.Background = primary ? Theme.Violet : Theme.Surface;
                button.Foreground = Brushes.White;
                button.BorderBrush = primary ? Theme.Violet : Theme.Border;
            };
            return button;
        }

        private static bool IsKnownInterval(int value)
        {
            return value == 15 || value == 30 || value == 60 || value == 300;
        }
    }
}
