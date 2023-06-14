using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace HttpCat.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HttpCatController : ControllerBase
    {
        private readonly ILogger<HttpCatController> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly HttpClient _httpClient;
        private readonly MemoryCacheEntryOptions _cacheEntryOptions;

        public HttpCatController(ILogger<HttpCatController> logger, IMemoryCache memoryCache, HttpClient httpClient)
        {
            _logger = logger;
            _memoryCache = memoryCache;
            _httpClient = httpClient;
            _cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(500);
        }

        [HttpGet("{url}")]
        public async Task<IActionResult> GetHttpCatImage(string url)
        {
            try
            {
                var parsedUrl = Uri.UnescapeDataString(url);

                var statusCode = await QueryForStatusCode(parsedUrl);

                _logger.LogDebug("HTTP GET to ${Url} returned code {StatusCode}", parsedUrl, statusCode);

                if (_memoryCache.TryGetValue(statusCode, out byte[] imageBytes))
                {
                    _logger.LogDebug("Cache hit for {StatusCode} code", statusCode);
                    return File(imageBytes, "image/jpeg");
                }

                _logger.LogDebug("Cache miss for {StatusCode} code, querying a cat", statusCode);

                var httpCatUrl = $"https://http.cat/{statusCode}.jpg";
                var response = await _httpClient.GetAsync(httpCatUrl);
                response.EnsureSuccessStatusCode();
                imageBytes = await response.Content.ReadAsByteArrayAsync();

#pragma warning disable CS4014
                Task.Run(() => // Intentionally run without awaiting
#pragma warning restore CS4014
                {
                    _memoryCache.Set(statusCode, imageBytes, _cacheEntryOptions);
                });

                return File(imageBytes, "image/jpeg");
            }
            catch (HttpQueryFailed ex)
            {
                _logger.LogError(ex, "Failed on query for status code");
                return Problem(
                    detail: $"Failed on query to {url}; {ex.InnerException?.Message}",
                    statusCode: 503
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "An error occurred while querying http.cat");
                return Problem(
                    detail: "An error occurred while querying http.cat",
                    statusCode: 503);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "An error occurred while parsing HTTP response");
                return Problem(
                    detail: "An error occurred while parsing HTTP response",
                    statusCode: 400);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred");
                return Problem(
                    detail: $"An error occurred: {ex.Message}",
                    statusCode: 500);
            }
        }

        private async Task<int> QueryForStatusCode(string url)
        {
            try
            {
                _logger.LogDebug("Quering {Url}", url);

                // We instrested only in status code so why query full page?
                var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                // var response = await _httpClient.GetAsync(parsedUrl);
                return (int)response.StatusCode;
            }
            catch (HttpRequestException ex)
            {
                throw new HttpQueryFailed("HTTP query failed", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new HttpQueryFailed("HTTP query failed", ex);
            }
        }
    }
}


public class HttpQueryFailed : Exception
{
    public HttpQueryFailed(string message, Exception innerException) : base(message, innerException) {}
}