using System.Buffers;
using System.Security.Cryptography;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Net;

/// <summary>
/// Streams a download to a temp file while computing its SHA-512, and verifies it against the expected
/// hash from the source (defeating tampering/MITM). Concurrency is bounded by the "concurrent
/// downloads" setting and tracks live changes to it.
/// </summary>
public sealed class HttpDownloader : IDownloader, IDisposable
{
    private readonly HttpClient _http;
    private readonly ISettingsStore _settings;
    private readonly object _gateLock = new();
    private SemaphoreSlim _gate;
    private int _limit;

    public HttpDownloader(HttpClient http, ISettingsStore settings)
    {
        _http = http;
        _settings = settings;
        _limit = Math.Clamp(settings.Current.ConcurrentDownloads,
            LodestoneSettings.MinConcurrentDownloads, LodestoneSettings.MaxConcurrentDownloads);
        _gate = new SemaphoreSlim(_limit, LodestoneSettings.MaxConcurrentDownloads);
        _settings.Changed += OnSettingsChanged;
    }

    public async Task<Result<DownloadedFile>> DownloadAsync(
        DownloadRequest request,
        IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        SemaphoreSlim gate = Volatile.Read(ref _gate);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await DownloadCoreAsync(request, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<DownloadedFile>> DownloadCoreAsync(
        DownloadRequest request,
        IProgress<TransferProgress>? progress,
        CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetTempPath(), "lodestone-downloads");
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{SanitizeFileName(request.FileName)}");

        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<DownloadedFile>("download.http", $"Download failed (HTTP {(int)response.StatusCode}).");
            }

            long? total = response.Content.Headers.ContentLength;
            string hash = await StreamToFileAsync(response, tempPath, total, progress, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(request.ExpectedSha512) &&
                !hash.Equals(request.ExpectedSha512, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(tempPath);
                return Result.Failure<DownloadedFile>("download.hash_mismatch",
                    "The downloaded file failed its integrity check and was discarded.");
            }

            long size = new FileInfo(tempPath).Length;
            return Result.Success(new DownloadedFile(tempPath, size, hash));
        }
        catch (HttpRequestException ex)
        {
            TryDelete(tempPath);
            return Result.Failure<DownloadedFile>("download.network", ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(tempPath);
            return Result.Failure<DownloadedFile>("download.timeout", "The download timed out.");
        }
    }

    private static async Task<string> StreamToFileAsync(
        HttpResponseMessage response,
        string tempPath,
        long? total,
        IProgress<TransferProgress>? progress,
        CancellationToken ct)
    {
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        await using Stream network = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream file = File.Create(tempPath);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long received = 0;
            int read;
            while ((read = await network.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                hasher.AppendData(buffer, 0, read);
                received += read;
                progress?.Report(new TransferProgress(received, total));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private void OnSettingsChanged(object? sender, LodestoneSettings settings)
    {
        int target = Math.Clamp(settings.ConcurrentDownloads,
            LodestoneSettings.MinConcurrentDownloads, LodestoneSettings.MaxConcurrentDownloads);

        lock (_gateLock)
        {
            if (target == _limit)
            {
                return;
            }

            _limit = target;
            // New downloads use the new gate; in-flight ones keep releasing on the old one safely.
            Volatile.Write(ref _gate, new SemaphoreSlim(target, LodestoneSettings.MaxConcurrentDownloads));
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _gate.Dispose();
    }
}
