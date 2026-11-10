using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace Appwebbongda.Controllers;

[ApiController]
[Route("api/admin/tiktok")]
[Authorize(Policy = "TikTokAdmin")]
[EnableRateLimiting("tiktok")]
public sealed class TikTokDownloaderController(
    IHttpClientFactory clients,
    IMemoryCache cache,
    ILogger<TikTokDownloaderController> logger) : ControllerBase
{
    private const long MaxVideoBytes = 100 * 1024 * 1024;
    private static readonly Uri ProviderEndpoint = new("https://www.tikwm.com/api/");
    private static readonly HashSet<string> TikTokHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiktok.com", "www.tiktok.com", "m.tiktok.com", "vm.tiktok.com", "vt.tiktok.com"
    };
    private static readonly string[] MediaHosts =
    ["tikwm.com", "tiktokcdn.com", "tiktokcdn-us.com", "tiktokcdn-eu.com", "byteoversea.com", "ibytedtos.com", "muscdn.com"];

    public sealed class ResolveRequest
    {
        [Required, StringLength(2048)]
        public string Url { get; set; } = "";
    }

    public sealed class DownloadRequest
    {
        [Required, RegularExpression("^[A-F0-9]{48}$")]
        public string DownloadToken { get; set; } = "";
    }

    private sealed record DownloadTicket(string Owner, Uri MediaUrl, string FileName);

    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve(ResolveRequest request, CancellationToken cancellationToken)
    {
        if (!TryVideoUrl(request.Url, out var videoUrl))
            return BadRequest(new { message = "Vui lòng nhập link video TikTok hợp lệ (https://...)." });

        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(owner)) return Unauthorized();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(35));
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["url"] = videoUrl!.AbsoluteUri,
                ["hd"] = "1"
            });
            var client = clients.CreateClient("tiktok-downloader");
            using var response = await client.PostAsync(ProviderEndpoint, form, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return StatusCode(502, new { message = "Dịch vụ lấy video đang bận. Vui lòng thử lại sau." });

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            var root = json.RootElement;
            if (!root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var resultCode)
                || resultCode != 0 || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
                return UnprocessableEntity(new { message = "Không lấy được video. Kiểm tra link và bảo đảm video đang công khai." });

            if (data.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
                return UnprocessableEntity(new { message = "Link này là bài đăng ảnh. Tính năng hiện hỗ trợ video TikTok." });

            // play/hdplay are the original videos. Never fall back to wmplay (watermarked).
            var hd = Text(data, "hdplay");
            var media = !string.IsNullOrWhiteSpace(hd) ? hd : Text(data, "play");
            if (!TryMediaUrl(media, out var mediaUrl))
                return UnprocessableEntity(new { message = "Video này chưa có bản tải không logo khả dụng." });

            var size = Number(data, !string.IsNullOrWhiteSpace(hd) ? "hd_size" : "size");
            if (size > MaxVideoBytes)
                return StatusCode(413, new { message = "Video vượt giới hạn tải 100 MB." });

            var id = Text(data, "id");
            if (!Regex.IsMatch(id, "^[0-9]{1,30}$")) id = Guid.NewGuid().ToString("N");
            var fileName = $"TikTok_{id}.mp4";
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            cache.Set($"tiktok:{ticket}", new DownloadTicket(owner, mediaUrl!, fileName), TimeSpan.FromMinutes(15));
            var author = data.TryGetProperty("author", out var authorData) && authorData.ValueKind == JsonValueKind.Object
                ? Text(authorData, "nickname") : "";
            var cover = TryMediaUrl(Text(data, "cover"), out var coverUrl) ? coverUrl!.AbsoluteUri : "";
            Response.Headers.CacheControl = "no-store";
            return Ok(new
            {
                downloadToken = ticket, fileName, title = Text(data, "title"), author,
                coverUrl = cover, duration = Number(data, "duration"), sizeBytes = size,
                quality = string.IsNullOrWhiteSpace(hd) ? "SD" : "HD"
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Lấy video quá lâu. Vui lòng thử lại." });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("TikTok resolve failed: {ErrorType}", ex.GetType().Name);
            return StatusCode(502, new { message = "Chưa kết nối được dịch vụ lấy video. Vui lòng thử lại sau." });
        }
    }

    [HttpPost("download")]
    public async Task<IActionResult> Download(DownloadRequest request, CancellationToken cancellationToken)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!cache.TryGetValue<DownloadTicket>($"tiktok:{request.DownloadToken}", out var ticket)
            || ticket is null || ticket.Owner != owner)
            return NotFound(new { message = "Link tải đã hết hạn. Bấm “Lấy video” để tạo link mới." });

        FileStream? file = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var client = clients.CreateClient("tiktok-downloader");
            using var response = await GetMedia(client, ticket.MediaUrl, timeout.Token);
            if (response is null || !response.IsSuccessStatusCode)
                return StatusCode(502, new { message = "Link video không còn khả dụng. Bấm “Lấy video” rồi thử lại." });

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("video/mp4" or "application/octet-stream"))
                return StatusCode(502, new { message = "Dịch vụ trả về định dạng không phải video MP4." });
            if (response.Content.Headers.ContentLength > MaxVideoBytes)
                return StatusCode(413, new { message = "Video vượt giới hạn tải 100 MB." });

            // A bounded temporary file avoids buffering a large MP4 in server memory.
            // ASP.NET disposes the returned stream; DeleteOnClose removes the temporary file.
            file = new FileStream(Path.Combine(Path.GetTempPath(), $"pnh-tiktok-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
            {
                total += read;
                if (total > MaxVideoBytes)
                    return StatusCode(413, new { message = "Video vượt giới hạn tải 100 MB." });
                await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            if (total == 0) return StatusCode(502, new { message = "Video trả về trống. Vui lòng lấy video lại." });

            file.Position = 0;
            Response.Headers.CacheControl = "no-store";
            var result = File(file, "video/mp4", ticket.FileName);
            file = null; // Ownership moves to FileStreamResult.
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Tải video quá lâu. Vui lòng thử lại." });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            logger.LogWarning("TikTok download failed: {ErrorType}", ex.GetType().Name);
            return StatusCode(502, new { message = "Tải video thất bại. Vui lòng thử lại." });
        }
        finally
        {
            if (file is not null) await file.DisposeAsync();
        }
    }

    private static async Task<HttpResponseMessage?> GetMedia(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!TryMediaUrl(uri.AbsoluteUri, out _)) return null;
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) return null;
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        return null;
    }

    private static bool TryVideoUrl(string value, out Uri? uri)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
            && TikTokHosts.Contains(uri.IdnHost) && uri.AbsolutePath.Length > 1;
    }

    private static bool TryMediaUrl(string value, out Uri? uri)
    {
        // Some TikWM responses use relative links; only resolve these against TikWM.
        if (value.StartsWith('/') && !value.StartsWith("//")) value = new Uri(ProviderEndpoint, value).AbsoluteUri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var hostName = uri.IdnHost;
        return MediaHosts.Any(host => hostName.Equals(host, StringComparison.OrdinalIgnoreCase)
            || hostName.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }

    private static string Text(JsonElement data, string key)
    {
        return data.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString() : "";
    }

    private static long Number(JsonElement data, string key) => long.TryParse(Text(data, key), out var value) ? value : 0;
}
