using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Controls;

/// <summary>
/// Product image gallery: shows every image linked to one product through ProductFiles
/// (the bytes themselves live in the application's UploadedFiles folder), lets the user add
/// one or more new images — copied to that folder, registered in UploadedFiles and linked
/// with fresh ProductFiles rows — and remove a link from the product.
/// </summary>
public partial class ProductImagesWindow : Window
{
    private readonly int _productId;

    private sealed class ImageItem
    {
        public int ProductFileId { get; init; }
        public string Name { get; init; } = "";
        public string Info { get; init; } = "";
        public string Path { get; init; } = "";
        public BitmapImage? Thumb { get; init; }
    }

    public ProductImagesWindow(int productId, string productName)
    {
        InitializeComponent();
        _productId = productId;
        var title = $"تصاویر کالا «{productName}»";
        Title = title;
        HeaderTitle.Text = title;

        BtnAdd.Click += (_, _) => Add();
        BtnDelete.Click += (_, _) => Delete();
        BtnReload.Click += (_, _) => Reload();
        ImagesList.SelectionChanged += (_, _) => ShowPreview();
        Loaded += (_, _) => Reload();
    }

    private async void Reload()
    {
        try
        {
            await using var db = SadrDb.New();
            var files = await db.ProductFiles.AsNoTracking()
                .Where(f => !f.Deleted && f.ProductId == _productId)
                .Join(db.UploadedFiles.AsNoTracking().Where(u => !u.Deleted),
                      f => f.UploadedFileId, u => u.Id,
                      (f, u) => new { LinkId = f.Id, u.FileName, u.OriginalName, u.FileSize })
                .OrderByDescending(x => x.LinkId)
                .ToListAsync();

            var items = new List<ImageItem>();
            foreach (var f in files)
            {
                var path = AppFiles.FullPath(f.FileName);
                BitmapImage? thumb = null;
                string info;

                if (!File.Exists(path))
                {
                    info = "فایل روی دیسک پیدا نشد";
                }
                else
                {
                    var kb = new FileInfo(path).Length / 1024.0;
                    info = $"{kb:N0} کیلوبایت";
                    try { thumb = LoadThumb(path); }
                    catch { info = "تصویر قابل نمایش نیست"; }
                }

                items.Add(new ImageItem
                {
                    ProductFileId = f.LinkId,
                    Name = string.IsNullOrWhiteSpace(f.OriginalName) ? f.FileName : f.OriginalName,
                    Info = info,
                    Path = path,
                    Thumb = thumb
                });
            }

            ImagesList.ItemsSource = items;
            EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Shows the selected image (or an empty preview) in the side panel.</summary>
    private void ShowPreview()
    {
        if (ImagesList.SelectedItem is ImageItem item)
        {
            Preview.Source = item.Thumb;
            PreviewName.Text = item.Name;
            PreviewInfo.Text = item.Info;
            PreviewPath.Text = item.Path;
            BtnDelete.IsEnabled = true;
        }
        else
        {
            Preview.Source = null;
            PreviewName.Text = "";
            PreviewInfo.Text = "";
            PreviewPath.Text = "";
            BtnDelete.IsEnabled = false;
        }
    }

    /// <summary>Copy one or more picked images into the app folder and link them to this product.</summary>
    private async void Add()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "انتخاب تصاویر کالا",
            Multiselect = true,
            Filter = AppFiles.ImageFilter
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await using var db = SadrDb.New();
            var count = await AppFiles.AttachToProductAsync(db, _productId, dlg.FileNames);
            if (count == 0)
            {
                MessageBox.Show("هیچ فایلی انتخاب نشد.", "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در افزودن تصویر", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Unlinks the selected image from this product (the stored file itself is kept).</summary>
    private async void Delete()
    {
        if (ImagesList.SelectedItem is not ImageItem item)
        {
            MessageBox.Show("ابتدا یک تصویر را انتخاب کنید.", "اطلاع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"تصویر «{item.Name}» از این کالا حذف شود؟", "تأیید حذف",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await using var db = SadrDb.New();
            var link = await db.ProductFiles.FirstAsync(f => f.Id == item.ProductFileId);
            link.Deleted = true;
            link.UpdateDateTime = DateTime.Now;
            await db.SaveChangesAsync();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در حذف", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Decodes a copy of the file for the tiles and the preview (never holds the file open).</summary>
    private static BitmapImage LoadThumb(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = 640;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
