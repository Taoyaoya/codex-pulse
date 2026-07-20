using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CodexPulse
{
    internal static class Theme
    {
        public static readonly Brush Background = BrushFrom("#00101424");
        public static readonly Brush Surface = BrushFrom("#B8171C35");
        public static readonly Brush SurfaceHover = BrushFrom("#5C7C6CCB");
        public static readonly Brush Border = BrushFrom("#667B83AC");
        public static readonly Brush PrimaryText = BrushFrom("#F8FAFF");
        public static readonly Brush SecondaryText = BrushFrom("#C2C8DC");
        public static readonly Brush MutedText = BrushFrom("#8992B0");
        public static readonly Brush Emerald = BrushFrom("#39E6AE");
        public static readonly Brush Cyan = BrushFrom("#3AA8FF");
        public static readonly Brush Violet = BrushFrom("#8B7CFF");
        public static readonly Brush Warning = BrushFrom("#F6B94C");

        public static Brush BrushFrom(string value)
        {
            return (Brush)new BrushConverter().ConvertFromString(value);
        }

        public static Brush WindowGlass()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(ColorFrom("#E4282A4A"), 0));
            brush.GradientStops.Add(new GradientStop(ColorFrom("#D9131730"), 0.54));
            brush.GradientStops.Add(new GradientStop(ColorFrom("#E10A1025"), 1));
            return brush;
        }

        public static Brush CardGlass(bool quota)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(ColorFrom("#B9343A58"), 0));
            brush.GradientStops.Add(new GradientStop(ColorFrom(quota ? "#C017293D" : "#C01B1C43"), 0.62));
            brush.GradientStops.Add(new GradientStop(ColorFrom(quota ? "#A7123A39" : "#A7221A55"), 1));
            return brush;
        }

        public static Brush CardBorder(bool quota)
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(ColorFrom("#A8E6ECFF"), 0));
            brush.GradientStops.Add(new GradientStop(ColorFrom(quota ? "#A139E6AE" : "#A18B7CFF"), 0.75));
            brush.GradientStops.Add(new GradientStop(ColorFrom("#4A586585"), 1));
            return brush;
        }

        public static Button IconButton(string text, string tooltip)
        {
            Button button = BaseButton(text, tooltip);
            button.Width = 38;
            button.Height = 34;
            button.FontSize = 15;
            button.Margin = new Thickness(3, 0, 0, 0);
            return button;
        }

        public static Button CompactButton(string text, string tooltip)
        {
            Button button = BaseButton(text, tooltip);
            button.MinWidth = 70;
            button.Height = 32;
            button.FontSize = 12;
            button.Padding = new Thickness(10, 0, 10, 0);
            button.Margin = new Thickness(4, 0, 8, 0);
            return button;
        }

        private static Button BaseButton(string text, string tooltip)
        {
            Button button = new Button
            {
                Content = text,
                ToolTip = tooltip,
                Foreground = SecondaryText,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = RoundedButtonTemplate()
            };
            button.MouseEnter += delegate
            {
                button.Background = SurfaceHover;
                button.BorderBrush = Border;
                button.Foreground = PrimaryText;
            };
            button.MouseLeave += delegate
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
                button.Foreground = SecondaryText;
            };
            return button;
        }

        private static ControlTemplate RoundedButtonTemplate()
        {
            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            borderFactory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(10));
            borderFactory.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            borderFactory.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            borderFactory.SetBinding(System.Windows.Controls.Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            borderFactory.SetBinding(System.Windows.Controls.Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenter);
            return new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
        }

        public static TextBlock Text(string value, double size, Brush color, FontWeight weight)
        {
            return new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontSize = size,
                Foreground = color,
                FontWeight = weight,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Color ColorFrom(string value)
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
    }
}
