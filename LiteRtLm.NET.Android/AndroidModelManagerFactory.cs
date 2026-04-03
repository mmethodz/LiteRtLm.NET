using System;
using System.IO;

namespace LiteRtLm.NET.Android;

/// <summary>
/// Factory to create ModelManager with Android-appropriate paths.
/// </summary>
public static class AndroidModelManagerFactory
{
    /// <summary>
    /// Creates a ModelManager using the app's internal files directory.
    /// </summary>
    public static ModelManager Create(string modelFileName)
    {
        var modelDir = Path.Combine(GetCacheDir(), "models");
        return new ModelManager(modelDir, modelFileName);
    }

    /// <summary>
    /// Gets the app's cache directory on Android.
    /// Falls back to local app data on non-Android (shouldn't happen).
    /// </summary>
    public static string GetCacheDir()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.CacheDir?.AbsolutePath
               ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    /// <summary>
    /// Gets the app's native library directory.
    /// Required for NPU backend on some SoCs.
    /// </summary>
    public static string GetNativeLibraryDir()
    {
        var ctx = global::Android.App.Application.Context;
        var appInfo = ctx.ApplicationInfo;
        return appInfo?.NativeLibraryDir
               ?? "/data/app/" + ctx.PackageName + "/lib/arm64";
    }
}
