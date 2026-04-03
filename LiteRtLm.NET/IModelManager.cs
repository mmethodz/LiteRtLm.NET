using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteRtLm.NET;

/// <summary>
/// Manages model file download, storage, and deletion.
/// Decoupled from inference — can download without loading.
/// </summary>
public interface IModelManager
{
    /// <summary>Whether the model file exists on disk.</summary>
    bool IsModelDownloaded { get; }

    /// <summary>Model file size on disk in bytes. 0 if not downloaded.</summary>
    long ModelSizeOnDisk { get; }

    /// <summary>Full path to the model file.</summary>
    string GetModelPath();

    /// <summary>Download model from URL to local storage. Reports progress 0.0-1.0.</summary>
    Task<bool> DownloadModelAsync(string url, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>Delete model file from local storage.</summary>
    Task DeleteModelAsync();
}
