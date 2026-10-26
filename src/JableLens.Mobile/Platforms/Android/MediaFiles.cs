using Android.Content;
using Android.OS;
using Android.Provider;
using AEnvironment = Android.OS.Environment;
using AUri = Android.Net.Uri;

namespace JableLens.Mobile.Platforms.Android;

/// <summary>
/// 下载好的视频：先在应用自己的目录里合并，完成后放进系统「下载 / Jable」文件夹（相册、文件管理、别的播放器都能看到）。
/// Android 10 以下没有 MediaStore 下载目录，就留在应用目录里。
/// </summary>
public static class MediaFiles
{
    private static Context Ctx => global::Android.App.Application.Context;

    /// <summary>合并用的工作目录（应用专属，不需要存储权限）。</summary>
    public static string WorkFolder =>
        Path.Combine(Ctx.GetExternalFilesDir(AEnvironment.DirectoryMovies)?.AbsolutePath ?? FileSystem.AppDataDirectory, "Jable");

    public static async Task<string> PublishAsync(string file, CancellationToken ct)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q) return file;
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, Path.GetFileName(file));
        values.Put(MediaStore.IMediaColumns.MimeType, file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "video/mp2t");
        values.Put(MediaStore.IMediaColumns.RelativePath, AEnvironment.DirectoryDownloads + "/Jable");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        var resolver = Ctx.ContentResolver!;
        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values) ?? throw new IOException("无法写入系统下载目录");
        try
        {
            await using (var output = resolver.OpenOutputStream(uri) ?? throw new IOException("无法写入系统下载目录"))
            await using (var input = File.OpenRead(file))
                await input.CopyToAsync(output, 1 << 20, ct);
            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
        File.Delete(file);
        return uri.ToString()!;
    }

    public static bool IsContentUri(string? path) => path?.StartsWith("content://", StringComparison.Ordinal) == true;

    public static void Delete(string path)
    {
        try
        {
            if (IsContentUri(path)) Ctx.ContentResolver!.Delete(AUri.Parse(path)!, null, null);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is Java.Lang.SecurityException or IOException) { }
    }

    /// <summary>用手机上的播放器打开下载好的文件。</summary>
    public static void Open(string path)
    {
        if (!IsContentUri(path))
        {
            // 应用目录里的文件（Android 10 以下）：交给系统的文件打开方式（会自动授权）
            _ = Launcher.Default.OpenAsync(new OpenFileRequest("用播放器打开", new ReadOnlyFile(path)));
            return;
        }
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(AUri.Parse(path), path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "video/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        Ctx.StartActivity(Intent.CreateChooser(intent, "用播放器打开")!.AddFlags(ActivityFlags.NewTask));
    }
}
