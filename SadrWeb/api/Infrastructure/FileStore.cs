using SadrWeb.Api.Data;

namespace SadrWeb.Api.Infrastructure;

/// <summary>
/// Stores uploaded product images on disk and returns the matching UploadedFiles fields.
/// Files get a unique GUID name (never the user's name, so nothing can escape the folder);
/// the original name is kept for display, exactly like the desktop AppFiles service.
/// </summary>
public class FileStore
{
    public static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    public const long MaxBytes = 8 * 1024 * 1024;

    private readonly string _root;
    public FileStore(string root) => _root = root;

    public string Root => _root;

    public async Task<UploadedFile> SaveAsync(Stream content, string originalName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("فرمت فایل مجاز نیست. فقط تصویر (png, jpg, gif, bmp, webp) قابل بارگذاری است.");

        var stored = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(_root, stored);
        await using (var target = File.Create(path))
        {
            await content.CopyToAsync(target, ct);
        }

        var size = new FileInfo(path).Length;
        if (size == 0)
        {
            File.Delete(path);
            throw new InvalidOperationException("فایل خالی است.");
        }
        if (size > MaxBytes)
        {
            File.Delete(path);
            throw new InvalidOperationException("حجم فایل بیش از حد مجاز (۸ مگابایت) است.");
        }

        return new UploadedFile
        {
            FileName = stored,
            OriginalName = Path.GetFileName(originalName),
            Extension = ext,
            FileSize = size,
            RecordUniqueId = Guid.NewGuid()
        };
    }

    public string? PhysicalPath(string storedName)
    {
        // storedName always comes from the database (a GUID name); still normalise the path.
        var safe = Path.GetFileName(storedName);
        var path = Path.Combine(_root, safe);
        return File.Exists(path) ? path : null;
    }

    public void DeleteQuietly(string storedName)
    {
        try
        {
            var path = PhysicalPath(storedName);
            if (path is not null) File.Delete(path);
        }
        catch
        {
            // A missing file must never break an API call.
        }
    }
}
