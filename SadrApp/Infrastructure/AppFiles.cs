using System.IO;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;

namespace SadrApp.Infrastructure;

/// <summary>
/// Storage for the files the application keeps (currently product images).
/// A picked file is copied into the "UploadedFiles" folder next to the executable under a
/// unique app-generated name, its record goes into the UploadedFiles table, and one
/// ProductFiles row is added per product so the image shows up in that product's gallery.
/// The original name is kept for display; the copy on disk is never touched by the UI.
/// </summary>
public static class AppFiles
{
    /// <summary>Folder (inside the application folder) that holds every stored file.</summary>
    public const string FolderName = "UploadedFiles";

    /// <summary>Absolute path of the storage folder; created on first use.</summary>
    public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName);

    /// <summary>Absolute path of a stored file (by its FileName column).</summary>
    public static string FullPath(string fileName) => Path.Combine(Folder, Path.GetFileName(fileName));

    /// <summary>Open-file filter used everywhere images are picked.</summary>
    public const string ImageFilter =
        "فایل‌های تصویری|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|همه فایل‌ها|*.*";

    /// <summary>
    /// Copies every picked file into the application folder, adds an UploadedFiles row per
    /// file and a ProductFiles row per (product, file), then saves. On any failure the
    /// copies made in this call are removed and nothing is written to the database.
    /// Returns how many files were attached.
    /// </summary>
    public static async Task<int> AttachToProductAsync(SadrDbContext db, int productId,
        IReadOnlyCollection<string> sourcePaths)
    {
        if (sourcePaths.Count == 0) return 0;

        Directory.CreateDirectory(Folder);
        var copied = new List<string>();
        var added = new List<object>();
        var attached = 0;

        try
        {
            var now = DateTime.Now;
            foreach (var src in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) continue;

                var ext = Path.GetExtension(src).ToLowerInvariant();
                var storedName = Guid.NewGuid().ToString("N") + ext;
                var dest = FullPath(storedName);
                File.Copy(src, dest, overwrite: false);
                copied.Add(dest);

                var file = new UploadedFile
                {
                    FileName = storedName,
                    OriginalName = Path.GetFileName(src),
                    Extension = ext,
                    FileSize = new FileInfo(dest).Length,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.UploadedFiles.Add(file);
                added.Add(file);

                var link = new ProductFile
                {
                    ProductId = productId,
                    UploadedFile = file,
                    RecordUniqueId = Guid.NewGuid(),
                    CreateDateTime = now
                };
                db.ProductFiles.Add(link);
                added.Add(link);
                attached++;
            }

            if (attached == 0) return 0;
            await db.SaveChangesAsync();
            return attached;
        }
        catch
        {
            // Roll back the copies and untrack the rows so a retry starts clean.
            foreach (var path in copied)
            {
                try { File.Delete(path); } catch { /* a leftover file is harmless */ }
            }
            foreach (var entity in added)
                db.Entry(entity).State = EntityState.Detached;
            throw;
        }
    }
}
