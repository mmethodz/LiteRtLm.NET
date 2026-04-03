using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LiteRtLm.NET;

/// <summary>
/// Shared model download and storage lifecycle manager.
/// Platform-agnostic file I/O — works on any platform with a writable filesystem.
/// </summary>
public class ModelManager : IModelManager
{
    private readonly string _modelDir;
    private readonly string _modelFileName;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a new ModelManager.
    /// </summary>
    /// <param name="modelDir">Absolute path to the directory where models are stored.</param>
    /// <param name="modelFileName">Model file name (e.g. "gemma-4-E2B-it-int4.litertlm").</param>
    /// <param name="httpClient">Optional HttpClient for downloads. A new instance is created if null.</param>
    public ModelManager(string modelDir, string modelFileName, HttpClient? httpClient = null)
    {
        _modelDir = modelDir ?? throw new ArgumentNullException(nameof(modelDir));
        _modelFileName = modelFileName ?? throw new ArgumentNullException(nameof(modelFileName));
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <inheritdoc />
    public bool IsModelDownloaded => File.Exists(GetModelPath());

    /// <inheritdoc />
    public long ModelSizeOnDisk => IsModelDownloaded
        ? new FileInfo(GetModelPath()).Length
        : 0;

    /// <inheritdoc />
    public string GetModelPath() => Path.Combine(_modelDir, _modelFileName);

    /// <inheritdoc />
    public async Task<bool> DownloadModelAsync(
        string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_modelDir);
            var tempPath = GetModelPath() + ".tmp";

            using var response = await _httpClient.GetAsync(url,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long bytesRead = 0;

            using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var fileStream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                bytesRead += read;
                if (totalBytes > 0)
                    progress?.Report((double)bytesRead / totalBytes);
            }

            // Atomic rename — prevents partial file on crash/cancel
            if (File.Exists(GetModelPath()))
                File.Delete(GetModelPath());
            File.Move(tempPath, GetModelPath());
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <inheritdoc />
    public Task DeleteModelAsync()
    {
        var path = GetModelPath();
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
