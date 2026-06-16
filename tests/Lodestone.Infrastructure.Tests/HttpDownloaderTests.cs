using System.Net;
using System.Text;
using Lodestone.Application.Abstractions;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Net;
using Lodestone.Infrastructure.Persistence;
using RichardSzalay.MockHttp;

namespace Lodestone.Infrastructure.Tests;

public class HttpDownloaderTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("pretend this is a mod jar");

    private static HttpDownloader Build(TempDir dir, MockHttpMessageHandler mock)
        => new(mock.ToHttpClient(), new JsonSettingsStore(dir.File("settings.json")));

    [Fact]
    public async Task Downloads_and_verifies_a_matching_sha512()
    {
        using var dir = new TempDir();
        var mock = new MockHttpMessageHandler();
        mock.When("https://cdn/sodium.jar")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
        using HttpDownloader downloader = Build(dir, mock);

        string expected = Hashing.Sha512Hex(Payload);
        Result<DownloadedFile> result = await downloader.DownloadAsync(
            new DownloadRequest("https://cdn/sodium.jar", "sodium.jar", expected));

        try
        {
            result.IsSuccess.ShouldBeTrue();
            result.Value.Sha512.ShouldBe(expected);
            File.Exists(result.Value.Path).ShouldBeTrue();
            (await File.ReadAllBytesAsync(result.Value.Path)).ShouldBe(Payload);
        }
        finally
        {
            if (result.IsSuccess)
            {
                File.Delete(result.Value.Path);
            }
        }
    }

    [Fact]
    public async Task Rejects_and_discards_a_mismatched_download()
    {
        using var dir = new TempDir();
        var mock = new MockHttpMessageHandler();
        mock.When("https://cdn/sodium.jar")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
        using HttpDownloader downloader = Build(dir, mock);

        Result<DownloadedFile> result = await downloader.DownloadAsync(
            new DownloadRequest("https://cdn/sodium.jar", "sodium.jar", "not-the-real-hash"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("download.hash_mismatch");
    }

    [Fact]
    public async Task A_http_error_becomes_a_typed_failure()
    {
        using var dir = new TempDir();
        var mock = new MockHttpMessageHandler();
        mock.When("https://cdn/missing.jar").Respond(HttpStatusCode.NotFound);
        using HttpDownloader downloader = Build(dir, mock);

        Result<DownloadedFile> result = await downloader.DownloadAsync(
            new DownloadRequest("https://cdn/missing.jar", "missing.jar"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("download.http");
    }
}
