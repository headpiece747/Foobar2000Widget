using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace Foobar2000Widget
{
    public class Foobar2000WidgetInstance : IWidgetInstance, IDisposable
    {
        public IWidgetObject WidgetObject { get; }
        public Guid Guid { get; }
        public WidgetSize WidgetSize { get; }
        public event WidgetUpdatedEventHandler WidgetUpdated;
        public IWidgetManager WidgetManager { get; set; }

        private readonly HttpClient _httpClient;
        private CancellationTokenSource _updateCts;
        private readonly SemaphoreSlim _updateSemaphore = new SemaphoreSlim(1, 1);
        private string _beefwebApiUrl = "http://localhost:8880/api";
        private bool _settingsLoaded;
        private bool _disposed;
        private bool _pollingStarted;   // prevents duplicate polling loops

        private PlayerState _currentPlayerState;
        private DateTime _lastPositionUpdateTime = DateTime.UtcNow;
        private string _currentTrackId = string.Empty;
        private Bitmap _currentAlbumArt;
        private Bitmap _prevIcon, _playIcon, _pauseIcon, _nextIcon;
        private string _currentTitle  = "Unknown Title";
        private string _currentArtist = "Unknown Album Artist";
        private string _currentAlbum  = "Unknown Album";
        private bool _albumArtLoadAttempted;
        private Color _currentWidgetBackgroundColor = Color.FromArgb(115, 128, 142);
        private Color _currentAccentColor = Color.FromArgb(115, 128, 142);
        private bool _hasConnectedSuccessfully;

        private readonly Font _titleFont       = new Font("Arial", 24, FontStyle.Bold);
        private readonly Font _infoFont        = new Font("Arial", 16);
        private readonly Font _timeFont        = new Font("Arial", 14);
        private readonly Font _fallbackArtFont = new Font("Arial", 72, FontStyle.Bold);
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public Foobar2000WidgetInstance(IWidgetObject widgetObject, WidgetSize widgetSize, Guid instanceGuid)
        {
            WidgetObject = widgetObject;
            WidgetSize   = widgetSize;
            Guid         = instanceGuid;

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _updateCts  = new CancellationTokenSource();

            LoadControlIcons();
        }

        // ── Polling ────────────────────────────────────────────────────────────────

        private void StartBackgroundPolling()
        {
            // Guard: only one polling loop at a time
            if (_pollingStarted || _disposed) return;
            _pollingStarted = true;

            var token = _updateCts.Token;
            Task.Run(async () =>
            {
                await Task.Delay(100, token);

                while (!_disposed && !token.IsCancellationRequested)
                {
                    try
                    {
                        if (!_settingsLoaded && WidgetManager != null)
                        {
                            LoadSettings();
                            _settingsLoaded = true;
                        }
                        await Task.Delay(1000, token);
                        if (token.IsCancellationRequested) break;
                        await UpdateWidgetAsync(token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Polling error: {ex.Message}");
                        try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; }
                    }
                }
                _pollingStarted = false;
            }, token);
        }

        public void RequestUpdate()
        {
            if (WidgetManager != null && !_settingsLoaded)
            {
                LoadSettings();
                _settingsLoaded = true;
            }

            // Ensure CTS is live
            if (_updateCts == null || _updateCts.IsCancellationRequested)
            {
                _updateCts?.Dispose();
                _updateCts = new CancellationTokenSource();
            }

            // Start background polling on first RequestUpdate
            StartBackgroundPolling();

            var token = _updateCts.Token;
            Task.Run(() => UpdateWidgetAsync(token), token);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public void EnterSleep()
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts     = new CancellationTokenSource();
            _pollingStarted = false;
            WidgetManager?.WriteLogMessage(this, LogLevel.INFO, "Entering sleep. Polling paused.");
        }

        public void ExitSleep()
        {
            if (_disposed) return;
            WidgetManager?.WriteLogMessage(this, LogLevel.INFO, "Exiting sleep. Resuming updates.");
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts      = new CancellationTokenSource();
            _pollingStarted  = false;
            StartBackgroundPolling();
            RequestUpdate();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _updateCts?.Cancel();
                try
                {
                    _updateSemaphore?.Wait(TimeSpan.FromSeconds(2));
                }
                catch { /* ignore if semaphore already disposed or timed out */ }

                try
                {
                    _updateCts?.Dispose();
                    _httpClient?.Dispose();
                    _currentAlbumArt?.Dispose();
                    _prevIcon?.Dispose();
                    _playIcon?.Dispose();
                    _pauseIcon?.Dispose();
                    _nextIcon?.Dispose();
                    _titleFont?.Dispose();
                    _infoFont?.Dispose();
                    _timeFont?.Dispose();
                    _fallbackArtFont?.Dispose();
                }
                finally
                {
                    _updateSemaphore?.Dispose();
                }
            }
            _prevIcon = _playIcon = _pauseIcon = _nextIcon = null;
            _currentAlbumArt   = null;
            _currentPlayerState = null;
        }

        public UserControl GetSettingsControl()
            => WidgetManager != null ? new Foobar2000WidgetSettings(this) : null;

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            const string defaultUrl = "http://localhost:8880/api";
            _beefwebApiUrl = defaultUrl;

            if (WidgetManager == null) return;

            if (WidgetManager.LoadSetting(this, "BeefwebApiUrl", out string value)
                && !string.IsNullOrWhiteSpace(value))
            {
                _beefwebApiUrl = value;
            }

            _beefwebApiUrl = _beefwebApiUrl.Trim().TrimEnd('/');
            WidgetManager.WriteLogMessage(this, LogLevel.INFO, $"API URL: {_beefwebApiUrl}");
        }

        // ── Icons ──────────────────────────────────────────────────────────────

        private void LoadControlIcons()
        {
            var assembly      = Assembly.GetExecutingAssembly();
            string ns         = GetType().Namespace?.Split('.')[0] ?? "Foobar2000Widget";

            _prevIcon  = LoadBitmapResource(assembly, $"{ns}.Resources.prev.png");
            _playIcon  = LoadBitmapResource(assembly, $"{ns}.Resources.play.png");
            _pauseIcon = LoadBitmapResource(assembly, $"{ns}.Resources.pause.png");
            _nextIcon  = LoadBitmapResource(assembly, $"{ns}.Resources.next.png");
        }

        private Bitmap LoadBitmapResource(Assembly assembly, string resourceName)
        {
            try
            {
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Resource not found: {resourceName}");
                        return null;
                    }
                    // Copy to MemoryStream so the Bitmap is not tied to the manifest stream
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var temp = new Bitmap(ms))
                        {
                            return new Bitmap(temp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Failed to load icon '{resourceName}': {ex.Message}");
                return null;
            }
        }

        // ── API ────────────────────────────────────────────────────────────────

        private async Task SendPlayerCommandAsync(string command, string label)
        {
            if (_disposed || string.IsNullOrEmpty(_beefwebApiUrl)) return;
            var cts = _updateCts;
            if (cts == null || cts.IsCancellationRequested) return;

            try
            {
                using (var response = await _httpClient.PostAsync(
                    $"{_beefwebApiUrl}/player/{command}", null, cts.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await Task.Delay(300, cts.Token).ConfigureAwait(false);
                    await UpdateWidgetAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"{label} failed: {ex.Message}");
            }
        }

        private async Task SetPlayerPositionAsync(double positionSeconds)
        {
            if (_disposed || string.IsNullOrEmpty(_beefwebApiUrl)) return;
            var cts = _updateCts;
            if (cts == null || cts.IsCancellationRequested) return;

            try
            {
                var requestBody = new SetPlayerStateRequest { Position = positionSeconds };
                string jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);
                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                using (var response = await _httpClient.PostAsync(
                    $"{_beefwebApiUrl}/player", content, cts.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (_currentPlayerState?.ActiveItem != null)
                    {
                        _currentPlayerState.ActiveItem.Position = positionSeconds;
                        _lastPositionUpdateTime = DateTime.UtcNow;
                    }
                    WidgetUpdated?.Invoke(this, new WidgetUpdatedEventArgs
                    {
                        WidgetBitmap = DrawWidget(),
                        Offset       = Point.Empty,
                        WaitMax      = 1000
                    });
                    await Task.Delay(200, cts.Token).ConfigureAwait(false);
                    await UpdateWidgetAsync(cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"SetPlayerPositionAsync failed: {ex.Message}");
            }
        }

        private async Task UpdateWidgetAsync(CancellationToken cancellationToken)
        {
            if (_disposed || cancellationToken.IsCancellationRequested) return;

            await _updateSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_disposed || cancellationToken.IsCancellationRequested) return;

                try
                {
                    await GetPlayerStateAsync(cancellationToken);
                    _hasConnectedSuccessfully = true;

                    if (cancellationToken.IsCancellationRequested) return;

                    string newTrackId  = GenerateTrackIdentifier();
                    bool trackChanged  = (_currentTrackId != newTrackId);
                    _currentTrackId    = newTrackId;

                    if (trackChanged && _currentPlayerState?.ActiveItem != null)
                    {
                        _albumArtLoadAttempted = false;
                        _currentAlbumArt?.Dispose();
                        _currentAlbumArt = null;
                        await GetAlbumArtAsync(
                            _currentPlayerState.ActiveItem.PlaylistId,
                            _currentPlayerState.ActiveItem.Index,
                            cancellationToken);
                    }
                    else if (_currentAlbumArt == null && !_albumArtLoadAttempted
                             && _currentPlayerState?.ActiveItem != null)
                    {
                        await GetAlbumArtAsync(
                            _currentPlayerState.ActiveItem.PlaylistId,
                            _currentPlayerState.ActiveItem.Index,
                            cancellationToken);
                    }
                    else if (_currentPlayerState?.ActiveItem == null && _currentAlbumArt != null)
                    {
                        _currentAlbumArt.Dispose();
                        _currentAlbumArt       = null;
                        _albumArtLoadAttempted  = false;
                        UpdateBackgroundColorFromAlbumArt();
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    _currentAlbumArt              = null;
                    if (_currentAlbumArt == null)
                    {
                        _currentWidgetBackgroundColor = Color.FromArgb(115, 128, 142);
                    }
                    _currentPlayerState            = null;
                }
                catch (Exception ex)
                {
                    WidgetManager?.WriteLogMessage(this, LogLevel.ERROR, $"Unexpected error: {ex}");
                    _currentWidgetBackgroundColor = Color.Black;
                    _currentTitle  = "Widget Error";
                    _currentArtist = "Check logs";
                    _currentAlbum  = string.Empty;
                }

                if (cancellationToken.IsCancellationRequested) return;

                Bitmap widgetBitmap = DrawWidget();
                if (widgetBitmap == null) return;

                WidgetUpdated?.Invoke(this, new WidgetUpdatedEventArgs
                {
                    WidgetBitmap = widgetBitmap,
                    Offset       = Point.Empty,
                    WaitMax      = 1000
                });
            }
            catch (OperationCanceledException) { }
            finally
            {
                _updateSemaphore.Release();
            }
        }

        private string SanitizeMetadata(string input, string fallback)
        {
            if (string.IsNullOrEmpty(input)) return fallback;
            string clean = new string(input.Where(c => !char.IsControl(c) || c == ' ').Take(256).ToArray());
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }

        private async Task GetPlayerStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string columnsParam = "%25title%25,%25album artist%25,%25album%25";
            string requestUrl   = $"{_beefwebApiUrl}/player?columns={columnsParam}&_={DateTime.Now.Ticks}";

            using (var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) return;

                var playerResponse  = JsonSerializer.Deserialize<GetPlayerStateResponse>(json, _jsonOptions);
                _currentPlayerState = playerResponse?.Player;
                _lastPositionUpdateTime = DateTime.UtcNow;

                if (_currentPlayerState?.ActiveItem?.Columns != null)
                {
                    var cols       = _currentPlayerState.ActiveItem.Columns;
                    _currentTitle  = cols.Count > 0 ? SanitizeMetadata(cols[0], "Unknown Title")        : "Unknown Title";
                    _currentArtist = cols.Count > 1 ? SanitizeMetadata(cols[1], "Unknown Album Artist") : "Unknown Album Artist";
                    _currentAlbum  = cols.Count > 2 ? SanitizeMetadata(cols[2], "Unknown Album")        : "Unknown Album";
                }
                else if (_currentPlayerState?.ActiveItem == null)
                {
                    _currentTitle  = (_currentPlayerState == null ||
                                      _currentPlayerState.PlaybackState == PlaybackState.stopped)
                                      ? "Foobar2000" : "No active track";
                    _currentArtist = string.Empty;
                    _currentAlbum  = string.Empty;
                    if (_currentAlbumArt != null)
                    {
                        _currentAlbumArt.Dispose();
                        _currentAlbumArt = null;
                        UpdateBackgroundColorFromAlbumArt();
                    }
                }
            }
        }

        private async Task GetAlbumArtAsync(string playlistId, int index, CancellationToken cancellationToken)
        {
            _albumArtLoadAttempted = true;
            if (string.IsNullOrEmpty(playlistId) || string.IsNullOrEmpty(_beefwebApiUrl))
            {
                _currentAlbumArt?.Dispose();
                _currentAlbumArt = null;
                UpdateBackgroundColorFromAlbumArt();
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();

            string url = $"{_beefwebApiUrl}/artwork/{Uri.EscapeDataString(playlistId)}/{index}?t={DateTime.Now.Ticks}";

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                        { NoCache = true, NoStore = true };

                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                                WidgetManager?.WriteLogMessage(this, LogLevel.WARN,
                                    $"Album art request failed ({playlistId}/{index}): {response.StatusCode}");

                            _albumArtLoadAttempted = false;
                            _currentAlbumArt?.Dispose();
                            _currentAlbumArt = null;
                            UpdateBackgroundColorFromAlbumArt();
                            return;
                        }

                        if (response.Content.Headers.ContentLength > 10 * 1024 * 1024)
                        {
                            WidgetManager?.WriteLogMessage(this, LogLevel.WARN, "Album art exceeds 10MB limit.");
                            _albumArtLoadAttempted = false;
                            return;
                        }

                        using (var ms = new MemoryStream())
                        {
                            await response.Content.CopyToAsync(ms).ConfigureAwait(false);
                            if (cancellationToken.IsCancellationRequested) return;

                            _currentAlbumArt?.Dispose();
                            if (ms.Length > 0)
                            {
                                ms.Position = 0;
                                using (var temp = new Bitmap(ms))
                                    _currentAlbumArt = new Bitmap(temp);
                            }
                            else
                            {
                                _currentAlbumArt = null;
                            }
                        }
                    }
                }
                UpdateBackgroundColorFromAlbumArt();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN,
                    $"Error fetching album art ({playlistId}/{index}): {ex.Message}");
                _albumArtLoadAttempted = false;
                _currentAlbumArt?.Dispose();
                _currentAlbumArt = null;
                UpdateBackgroundColorFromAlbumArt();
            }
        }

        private void UpdateBackgroundColorFromAlbumArt()
        {
            if (_currentAlbumArt == null)
            {
                _currentWidgetBackgroundColor = Color.FromArgb(115, 128, 142);
                return;
            }

            try
            {
                using (var sample = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(sample))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_currentAlbumArt, new Rectangle(0, 0, 32, 32));

                    var colorBuckets = new Dictionary<int, (Color color, int count, float brightness)>();

                    for (int y = 0; y < sample.Height; y++)
                    {
                        for (int x = 0; x < sample.Width; x++)
                        {
                            Color px = sample.GetPixel(x, y);
                            float brightness = px.GetBrightness();

                            // Filter out extreme blacks/whites so borders or blown-out highlights don't override actual colors
                            if (brightness < 0.10f || brightness > 0.92f)
                                continue;

                            // Quantize color by rounding R, G, B to nearest 16 to group similar shades
                            int quantR = (px.R / 16) * 16;
                            int quantG = (px.G / 16) * 16;
                            int quantB = (px.B / 16) * 16;
                            int key = (quantR << 16) | (quantG << 8) | quantB;

                            if (colorBuckets.TryGetValue(key, out var bucket))
                            {
                                if (brightness > bucket.brightness)
                                {
                                    colorBuckets[key] = (px, bucket.count + 1, brightness);
                                }
                                else
                                {
                                    colorBuckets[key] = (bucket.color, bucket.count + 1, bucket.brightness);
                                }
                            }
                            else
                            {
                                colorBuckets[key] = (px, 1, brightness);
                            }
                        }
                    }

                    if (colorBuckets.Count > 0)
                    {
                        // 1. First, check for vibrant/colorful shades (saturation >= 0.22 and brightness in readable range)
                        // This prevents desaturated skin tones, white microphones, or muddy grey highlights from beating true artwork colors
                        var colorfulBuckets = colorBuckets.Values
                            .Where(b => b.color.GetSaturation() >= 0.22f && b.color.GetBrightness() >= 0.18f && b.color.GetBrightness() <= 0.85f)
                            .ToList();

                        Color selectedColor;
                        if (colorfulBuckets.Count > 0)
                        {
                            // Score vibrant colors by combining brightness, saturation, and presence frequency
                            var bestBucket = colorfulBuckets
                                .OrderByDescending(b => b.color.GetBrightness() * (0.5f + 0.5f * b.color.GetSaturation()) * (1.0f + Math.Min(0.5f, b.count / 50.0f)))
                                .First();
                            selectedColor = bestBucket.color;
                        }
                        else
                        {
                            // Fallback if artwork is truly monochrome or desaturated: pick cleanest brightest neutral
                            var bestBucket = colorBuckets.Values
                                .OrderByDescending(b => b.color.GetBrightness())
                                .ThenByDescending(b => b.count)
                                .First();
                            selectedColor = bestBucket.color;
                        }

                        // Because text is fixed to crisp white (Brushes.White), if the selected color is
                        // extremely light (> 0.65 brightness), gently cap at 0.65 to guarantee sharp text contrast
                        float currentBrightness = selectedColor.GetBrightness();
                        if (currentBrightness > 0.65f)
                        {
                            _currentWidgetBackgroundColor = AdjustBrightness(selectedColor, 0.65f);
                        }
                        else
                        {
                            _currentWidgetBackgroundColor = selectedColor;
                        }
                    }
                    else
                    {
                        // Fallback if artwork is dark or unreadable
                        Color px = sample.GetPixel(16, 16);
                        _currentWidgetBackgroundColor = px;
                    }
                }
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Color extraction error: {ex.Message}");
                _currentWidgetBackgroundColor = Color.FromArgb(115, 128, 142);
            }
        }

        private Color AdjustBrightness(Color color, float targetBrightness)
        {
            float currentBrightness = color.GetBrightness();
            if (currentBrightness <= 0.001f) return color;

            float factor = targetBrightness / currentBrightness;
            int r = Math.Min(255, Math.Max(0, (int)(color.R * factor)));
            int g = Math.Min(255, Math.Max(0, (int)(color.G * factor)));
            int b = Math.Min(255, Math.Max(0, (int)(color.B * factor)));
            return Color.FromArgb(255, r, g, b);
        }

        private string GenerateTrackIdentifier()
        {
            if (_currentPlayerState?.ActiveItem == null) return string.Empty;
            var item    = _currentPlayerState.ActiveItem;
            var cols    = item.Columns;
            string meta = cols != null && cols.Count > 0
                ? string.Join(":", cols.Take(3).Select(c => c ?? string.Empty))
                : string.Empty;
            return $"{item.PlaylistId ?? "N/A"}:{item.Index}:{meta}";
        }

        // ── Layout & Drawing ───────────────────────────────────────────────────

        // StringFormat is IDisposable — must NOT be stored in a struct field across calls.
        // CalculateLayout now returns only geometry; callers create their own StringFormat.
        private struct WidgetLayout
        {
            public Rectangle  AlbumArtRect;
            public RectangleF TitleRect;
            public RectangleF ArtistRect;
            public RectangleF AlbumRect;
            public Rectangle  PrevButtonRect;
            public Rectangle  PlayPauseButtonRect;
            public Rectangle  NextButtonRect;
        }

        private WidgetLayout CalculateLayout()
        {
            var layout = new WidgetLayout();
            Size size  = WidgetSize.ToSize();
            if (size.Width <= 0 || size.Height <= 0) return layout;

            int padding     = 10;
            int albumArtSide = Math.Max(0,
                Math.Min(size.Width - 2 * padding, size.Height - 2 * padding));

            layout.AlbumArtRect = new Rectangle(padding, padding, albumArtSide, albumArtSide);

            int textX     = layout.AlbumArtRect.Right + 35;
            int textWidth = Math.Max(0, size.Width - textX - padding);
            if (textWidth <= 0) return layout;

            int titleH      = _titleFont.Height;
            int infoH       = _infoFont.Height;
            int blockH      = titleH + infoH * 2 + 10;
            int albumCenterY = layout.AlbumArtRect.Top + layout.AlbumArtRect.Height / 2;
            int textTopY    = Math.Max(padding,
                Math.Min(albumCenterY - blockH / 2,
                         Math.Max(padding, size.Height - padding - blockH - 100)));

            int y = textTopY;
            layout.TitleRect  = new RectangleF(textX, y, textWidth, titleH); y += titleH + 5;
            layout.ArtistRect = new RectangleF(textX, y, textWidth, infoH);  y += infoH  + 5;
            layout.AlbumRect  = new RectangleF(textX, y, textWidth, infoH);

            int btnSize    = 48;
            int btnSpacing = 28;
            int btnTopY    = Math.Max(padding, size.Height - padding - btnSize - 15);
            int totalW     = 3 * btnSize + 2 * btnSpacing;
            int btnStartX  = Math.Max(textX, textWidth > totalW
                ? textX + (textWidth - totalW) / 2
                : textX);

            layout.PrevButtonRect      = new Rectangle(btnStartX, btnTopY, btnSize, btnSize);
            layout.PlayPauseButtonRect = new Rectangle(layout.PrevButtonRect.Right + btnSpacing, btnTopY, btnSize, btnSize);
            layout.NextButtonRect      = new Rectangle(layout.PlayPauseButtonRect.Right + btnSpacing, btnTopY, btnSize, btnSize);

            return layout;
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type != ClickType.Single || _disposed) return;

            var layout = CalculateLayout();
            if (layout.AlbumArtRect.Width <= 0) return;

            var click = new Point(x, y);
            if      (layout.PrevButtonRect.Contains(click))      _ = SendPlayerCommandAsync("previous",   "Previous");
            else if (layout.PlayPauseButtonRect.Contains(click)) _ = SendPlayerCommandAsync("play-pause", "Play/Pause");
            else if (layout.NextButtonRect.Contains(click))      _ = SendPlayerCommandAsync("next",       "Next");
            else if (_currentPlayerState?.ActiveItem != null && _currentPlayerState.ActiveItem.Duration > 0)
            {
                int dividerY = (int)layout.PrevButtonRect.Top - 34;
                int barLeft  = (int)layout.TitleRect.Left;
                int barWidth = WidgetSize.ToSize().Width - 35 - barLeft;

                // +/- 16px vertical touch hit zone across the progress bar
                if (barWidth > 0 && y >= dividerY - 16 && y <= dividerY + 16 && x >= barLeft && x <= barLeft + barWidth)
                {
                    double ratio = Math.Max(0.0, Math.Min(1.0, (double)(x - barLeft) / barWidth));
                    double targetSeconds = ratio * _currentPlayerState.ActiveItem.Duration;
                    _ = SetPlayerPositionAsync(targetSeconds);
                }
            }
        }

        private Bitmap DrawWidget()
        {
            Size size = WidgetSize.ToSize();
            if (size.Width <= 0 || size.Height <= 0) return null;

            var layout = CalculateLayout();
            if (layout.AlbumArtRect.Width <= 0) return null;

            Bitmap bitmap = new Bitmap(size.Width, size.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (var g          = Graphics.FromImage(bitmap))
                using (var textFormat = new StringFormat    // created & disposed here, not in struct
                {
                    Trimming    = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                })
                {
                    g.SmoothingMode     = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.Clear(_currentWidgetBackgroundColor);

                    // 1. Floating Album Art Drop Shadow (~0.002 ms cost)
                    Rectangle shadowRect = new Rectangle(layout.AlbumArtRect.X + 4, layout.AlbumArtRect.Y + 4, layout.AlbumArtRect.Width, layout.AlbumArtRect.Height);
                    using (var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    {
                        g.FillRectangle(shadowBrush, shadowRect);
                    }

                    // Album art
                    if (_currentAlbumArt != null)
                    {
                        g.DrawImage(_currentAlbumArt, layout.AlbumArtRect);
                    }
                    else
                    {
                        // Option 3: Glassmorphic Music Card with double musical note (♫)
                        // 100% lightweight (exactly 1 FillRectangle + 1 DrawString) with zero performance overhead
                        using (var cardBrush = new SolidBrush(Color.FromArgb(140, 18, 18, 24)))
                            g.FillRectangle(cardBrush, layout.AlbumArtRect);

                        using (var sf = new StringFormat
                            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("♫", _fallbackArtFont, Brushes.White, layout.AlbumArtRect, sf);
                    }

                    // Crisp 1px subtle highlight border around artwork
                    using (var borderPen = new Pen(Color.FromArgb(45, 255, 255, 255), 1f))
                    {
                        g.DrawRectangle(borderPen, layout.AlbumArtRect);
                    }

                    // 2. Text Opacity Hierarchy: Title (100% White), Artist (90% White), Album (75% White)
                    if (layout.TitleRect.Width > 0)
                    {
                        g.DrawString(_currentTitle  ?? "Unknown Title",        _titleFont, Brushes.White, layout.TitleRect,  textFormat);
                        using (var artistBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                            g.DrawString(_currentArtist ?? "Unknown Album Artist",  _infoFont,  artistBrush,   layout.ArtistRect, textFormat);
                        using (var albumBrush  = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                            g.DrawString(_currentAlbum  ?? "Unknown Album",         _infoFont,  albumBrush,    layout.AlbumRect,  textFormat);
                    }

                    // 3. Control Deck Progress Bar with Timestamps (M:SS)
                    int dividerY = (int)layout.PrevButtonRect.Top - 34;
                    if (layout.TitleRect.Width > 0)
                    {
                        int barLeft  = (int)layout.TitleRect.Left;
                        int barWidth = size.Width - 35 - barLeft;

                        if (barWidth > 0)
                        {
                            // 1. Draw background track line (~3x larger: 6px thick with rounded ends)
                            using (var bgPen = new Pen(Color.FromArgb(40, 255, 255, 255), 6f))
                            {
                                bgPen.StartCap = LineCap.Round;
                                bgPen.EndCap   = LineCap.Round;
                                g.DrawLine(bgPen, barLeft, dividerY, barLeft + barWidth, dividerY);
                            }

                            // 2. Draw active progress bar fill and timestamps if duration is known and > 0
                            if (_currentPlayerState?.ActiveItem != null && _currentPlayerState.ActiveItem.Duration > 0)
                            {
                                double pos = _currentPlayerState.ActiveItem.Position;
                                double dur = _currentPlayerState.ActiveItem.Duration;
                                if (_currentPlayerState.PlaybackState == PlaybackState.playing)
                                {
                                    pos += (DateTime.UtcNow - _lastPositionUpdateTime).TotalSeconds;
                                }
                                double ratio = Math.Max(0.0, Math.Min(1.0, pos / dur));
                                int fillWidth = (int)Math.Round(barWidth * ratio);

                                if (fillWidth > 0)
                                {
                                    using (var fillPen = new Pen(Brushes.White, 6f))
                                    {
                                        fillPen.StartCap = LineCap.Round;
                                        fillPen.EndCap   = LineCap.Round;
                                        g.DrawLine(fillPen, barLeft, dividerY, barLeft + fillWidth, dividerY);
                                    }

                                    // Crisp scrubber handle dot (16px diameter) at the tip of progress bar
                                    int dotRadius = 8;
                                    g.FillEllipse(Brushes.White, barLeft + fillWidth - dotRadius, dividerY - dotRadius, dotRadius * 2, dotRadius * 2);
                                }

                                // 3. Draw M:SS Timestamps below ends of progress bar
                                string posStr = FormatTimeSpan(Math.Max(0.0, Math.Min(dur, pos)));
                                string durStr = FormatTimeSpan(dur);

                                using (var timeBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                                {
                                    int timeY = dividerY + 12;
                                    g.DrawString(posStr, _timeFont, timeBrush, barLeft, timeY, textFormat);

                                    using (var farFormat = new StringFormat { Alignment = StringAlignment.Far })
                                    {
                                        g.DrawString(durStr, _timeFont, timeBrush, barLeft + barWidth, timeY, farFormat);
                                    }
                                }
                            }
                        }
                    }

                    // Buttons with Circular Glass Pill behind PlayPause button
                    if (layout.PrevButtonRect.Width > 0)
                    {
                        var circleRect = new Rectangle(
                            layout.PlayPauseButtonRect.X - 6,
                            layout.PlayPauseButtonRect.Y - 6,
                            layout.PlayPauseButtonRect.Width + 12,
                            layout.PlayPauseButtonRect.Height + 12);

                        using (var pillBrush = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                        {
                            g.FillEllipse(pillBrush, circleRect);
                        }
                        using (var pillBorder = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                        {
                            g.DrawEllipse(pillBorder, circleRect);
                        }

                        if (_prevIcon != null) g.DrawImage(_prevIcon, layout.PrevButtonRect);

                        var playPauseIcon = _currentPlayerState?.PlaybackState == PlaybackState.playing
                            ? _pauseIcon : _playIcon;
                        if (playPauseIcon != null) g.DrawImage(playPauseIcon, layout.PlayPauseButtonRect);

                        if (_nextIcon != null) g.DrawImage(_nextIcon, layout.NextButtonRect);
                    }
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.ERROR, $"DrawWidget error: {ex}");
                bitmap.Dispose();
                return null;
            }
        }

        private static string FormatTimeSpan(double totalSeconds)
        {
            if (totalSeconds < 0 || double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds)) return "0:00";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
