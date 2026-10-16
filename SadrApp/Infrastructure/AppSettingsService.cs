using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SadrApp.Infrastructure;

/// <summary>One selectable color schema: chrome colors applied to the whole app.</summary>
public sealed class ThemePalette
{
    public string Name = "";
    public string Primary = "#2F6FED";        // buttons, accents
    public string PrimaryDark = "#1F3A5F";    // header bar, headings
    public string PrimaryHover = "#2458C4";
    public string PrimaryDisabled = "#A9BFEA";
    public string GhostBorder = "#C3D2F4";
    public string GhostHover = "#EDF3FF";
    public string PageBg = "#F3F5FA";
    public string StatusBar = "#E8EDF6";
}

/// <summary>Program-wide settings persisted next to the per-user font settings.</summary>
public sealed class AppSettings
{
    /// <summary>Seller name printed on invoices in place of «صدر»; empty = keep default.</summary>
    public string SellerName { get; set; } = "";

    /// <summary>File name (inside the SadrApp settings folder) of the invoice logo; empty = none.</summary>
    public string LogoFileName { get; set; } = "";

    /// <summary>Index into ThemeService.Palettes of the selected color schema.</summary>
    public int ThemeIndex { get; set; }
}

/// <summary>
/// Loads/saves app settings (%APPDATA%\SadrApp\app-settings.json) and applies the
/// selected color schema by swapping the themed brushes in Application resources.
/// Mirrors the FontSettingsService pattern: JSON file, tolerant of corruption.
/// </summary>
public static class AppSettingsService
{
    /// <summary>The five built-in color schemas, most-liked first (default blue).</summary>
    public static readonly ThemePalette[] Palettes =
    {
        new() { Name = "آبی (پیش‌فرض)",     Primary = "#2F6FED", PrimaryDark = "#1F3A5F", PrimaryHover = "#2458C4", PrimaryDisabled = "#A9BFEA", GhostBorder = "#C3D2F4", GhostHover = "#EDF3FF", PageBg = "#F3F5FA", StatusBar = "#E8EDF6" },
        new() { Name = "سبز زمردی",        Primary = "#2E9E5B", PrimaryDark = "#14532D", PrimaryHover = "#237C47", PrimaryDisabled = "#A9DCC1", GhostBorder = "#BEE7CF", GhostHover = "#E7F6EE", PageBg = "#F2FBF5", StatusBar = "#DEEFE5" },
        new() { Name = "بنفش سلطنتی",      Primary = "#7C3AED", PrimaryDark = "#2E1065", PrimaryHover = "#6428C9", PrimaryDisabled = "#C7B2F1", GhostBorder = "#DBCEF8", GhostHover = "#EFE9FD", PageBg = "#F6F4FB", StatusBar = "#E9E3F5" },
        new() { Name = "نارنجی گرم",       Primary = "#E97410", PrimaryDark = "#7C2D12", PrimaryHover = "#C55E08", PrimaryDisabled = "#F3C295", GhostBorder = "#F5D6B5", GhostHover = "#FAEEDF", PageBg = "#FBF4EC", StatusBar = "#F3E5D4" },
        new() { Name = "فیروزه‌ای",        Primary = "#0D9488", PrimaryDark = "#134E4A", PrimaryHover = "#0A7A70", PrimaryDisabled = "#9FD4CE", GhostBorder = "#BAE1DC", GhostHover = "#E4F3F1", PageBg = "#EFF8F7", StatusBar = "#DCEEEB" },
    };

    /// <summary>Settings currently in effect (last loaded or saved).</summary>
    public static AppSettings Current { get; private set; } = new();

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SadrApp");

    private static string FilePath => Path.Combine(Folder, "app-settings.json");

    /// <summary>Reads saved settings; missing or corrupt values fall back to defaults.</summary>
    public static AppSettings Load()
    {
        var d = new AppSettings();
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } loaded)
            {
                loaded.SellerName = loaded.SellerName?.Trim() ?? "";
                loaded.LogoFileName = SanitizeFileName(loaded.LogoFileName ?? "");
                if (loaded.ThemeIndex < 0 || loaded.ThemeIndex >= Palettes.Length) loaded.ThemeIndex = 0;
                Current = loaded;
            }
            else
            {
                Current = d;
            }
        }
        catch
        {
            Current = d; // corrupt/unreadable file → defaults
        }
        return Current;
    }

    /// <summary>Loads settings and applies the saved color schema (call once at startup).</summary>
    public static void LoadAndApply() => ApplyTheme(Load().ThemeIndex);

    /// <summary>Persists the settings and marks them current.</summary>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        Current = settings;
    }

    // ---------- color schemas ----------

    /// <summary>Applies palette[index] to the live application resources and persists the choice.</summary>
    public static void ApplyTheme(int index)
    {
        if (index < 0 || index >= Palettes.Length) index = 0;
        ApplyPalette(Palettes[index]);
        Current.ThemeIndex = index;
        Save(Current);
    }

    /// <summary>Swaps the themed brushes without persisting (used for live previews).</summary>
    public static void ApplyPalette(ThemePalette p)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;

        SetBrush(res, "PrimaryBrush", p.Primary);
        SetBrush(res, "PrimaryDarkBrush", p.PrimaryDark);
        SetBrush(res, "PrimaryHoverBrush", p.PrimaryHover);
        SetBrush(res, "PrimaryDisabledBrush", p.PrimaryDisabled);
        SetBrush(res, "GhostBorderBrush", p.GhostBorder);
        SetBrush(res, "GhostHoverBrush", p.GhostHover);
        SetBrush(res, "PageBgBrush", p.PageBg);
        SetBrush(res, "StatusBarBrush", p.StatusBar);
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        res[key] = brush;
    }

    // ---------- invoice logo ----------

    /// <summary>Full path where a picked logo image is copied to.</summary>
    public static string LogoPath(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Copies a picked image into the settings folder (max 2 MB) and stores its name.</summary>
    public static string SetLogo(string sourcePath)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        if (bytes.Length > 2 * 1024 * 1024)
            throw new InvalidOperationException("حجم تصویر بیشتر از ۲ مگابایت است.");

        Directory.CreateDirectory(Folder);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant() is { Length: > 1 } e ? e : ".png";
        var fileName = "logo" + ext;
        File.Copy(sourcePath, LogoPath(fileName), overwrite: true);

        Current.LogoFileName = fileName;
        Save(Current);
        return fileName;
    }

    /// <summary>Clears the saved logo.</summary>
    public static void ClearLogo()
    {
        if (Current.LogoFileName.Length > 0)
        {
            try { File.Delete(LogoPath(Current.LogoFileName)); } catch { }
        }
        Current.LogoFileName = "";
        Save(Current);
    }

    /// <summary>Logo bytes for printing; null when no logo was saved or the file is unreadable.</summary>
    public static byte[]? ReadLogo() => ReadLogoFile(Current.LogoFileName);

    public static byte[]? ReadLogoFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        try
        {
            var path = LogoPath(fileName);
            return File.Exists(path) && new FileInfo(path).Length > 0 ? File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }

    private static string SanitizeFileName(string name) =>
        Path.GetFileName(name.Replace('\\', '/'));
}
