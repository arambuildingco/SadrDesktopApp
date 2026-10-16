using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Pages;

/// <summary>
/// Program settings: seller name + invoice logo (used when printing) and the
/// app-wide color schema. Persisted by AppSettingsService (%APPDATA% JSON).
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();

        var s = AppSettingsService.Load();
        TblSellerName.Text = s.SellerName;
        ShowLogoPreview();

        BtnSave.Click += BtnSave_Click;
        BtnPickLogo.Click += BtnPickLogo_Click;
        BtnClearLogo.Click += (_, _) => { AppSettingsService.ClearLogo(); ShowLogoPreview(); };
        BtnPrintSettings.Click += (_, _) => new Views.PrintSettingsWindow
            { Owner = Window.GetWindow(this) }.ShowDialog();
        BtnFontSettings.Click += (_, _) => new Views.FontSettingsWindow
            { Owner = Window.GetWindow(this) }.ShowDialog();

        BuildThemeSwatches();
    }

    // ---------- seller name ----------

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettingsService.Load();
        s.SellerName = TblSellerName.Text.Trim();
        AppSettingsService.Save(s);
        TxtSavedStatus.Text = "ذخیره شد ✓";
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { TxtSavedStatus.Text = ""; t.Stop(); };
        t.Start();
    }

    // ---------- logo ----------

    private void BtnPickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "انتخاب لوگو",
            Filter = "تصویر (PNG, JPG)|*.png;*.jpg;*.jpeg"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            AppSettingsService.SetLogo(dlg.FileName);
            ShowLogoPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "خطا",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowLogoPreview()
    {
        var bytes = AppSettingsService.ReadLogo();
        if (bytes is not { Length: > 0 })
        {
            LogoPreview.Source = null;
            return;
        }
        try
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            LogoPreview.Source = img;
        }
        catch { LogoPreview.Source = null; }
    }

    // ---------- color schemas ----------

    /// <summary>Builds one swatch per palette: three color chips + name, click to apply.</summary>
    private void BuildThemeSwatches()
    {
        ThemePanel.Children.Clear();
        for (int i = 0; i < AppSettingsService.Palettes.Length; i++)
        {
            int index = i;
            var p = AppSettingsService.Palettes[i];
            var selected = AppSettingsService.Current.ThemeIndex == i;

            var card = new Button
            {
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White,
                BorderThickness = new Thickness(2),
                BorderBrush = selected
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Primary))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9E1EF")),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            var panel = new StackPanel();
            var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var hex in new[] { p.Primary, p.PrimaryDark, p.PageBg })
                chips.Children.Add(new Rectangle
                {
                    Width = 26, Height = 26, RadiusX = 4, RadiusY = 4, Margin = new Thickness(2, 0, 2, 0),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9D2E3"))
                });
            panel.Children.Add(chips);
            panel.Children.Add(new TextBlock
            {
                Text = p.Name, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44506B"))
            });
            card.Content = panel;
            card.Click += (_, _) =>
            {
                AppSettingsService.ApplyTheme(index);
                BuildThemeSwatches();
            };
            ThemePanel.Children.Add(card);
        }
    }
}
