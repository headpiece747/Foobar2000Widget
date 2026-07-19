using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
        private string _currentTrackId = string.Empty;
        private Bitmap _currentAlbumArt;
        private Bitmap _prevIcon, _playIcon, _pauseIcon, _nextIcon;
        private string _currentTitle  = "Unknown Title";
        private string _currentArtist = "Unknown Album Artist";
        private string _currentAlbum  = "Unknown Album";
        private bool _albumArtLoadAttempted;
        private Color _currentWidgetBackgroundColor = Color.Black;
        private bool _hasConnectedSuccessfully;

        private readonly Font _titleFont = new Font("Arial", 24, FontStyle.Bold);
        private readonly Font _infoFont  = new Font("Arial", 20);
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
                        await Task.Delay(2000, token);
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
                    WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Connection error: {ex.Message}");
                    _currentTitle  = _hasConnectedSuccessfully ? "Foobar was closed" : "Foobar is not running";
                    _currentArtist = string.Empty;
                    _currentAlbum  = string.Empty;
                    _currentAlbumArt?.Dispose();
                    _currentAlbumArt              = null;
                    _currentWidgetBackgroundColor  = Color.Black;
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
                _currentWidgetBackgroundColor = Color.Black;
                return;
            }
            try
            {
                using (var tinyArt = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(tinyArt))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_currentAlbumArt, new Rectangle(0, 0, 1, 1));
                    var px = tinyArt.GetPixel(0, 0);
                    _currentWidgetBackgroundColor = Color.FromArgb(255, px.R, px.G, px.B);
                }
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Color extraction error: {ex.Message}");
                _currentWidgetBackgroundColor = Color.Black;
            }
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

            int textX     = layout.AlbumArtRect.Right + padding;
            int textWidth = Math.Max(0, size.Width - textX - padding);
            if (textWidth <= 0) return layout;

            int titleH      = _titleFont.Height;
            int infoH       = _infoFont.Height;
            int blockH      = titleH + infoH * 2 + 10;
            int albumCenterY = layout.AlbumArtRect.Top + layout.AlbumArtRect.Height / 2;
            int textTopY    = Math.Max(padding,
                Math.Min(albumCenterY - blockH / 2,
                         Math.Max(padding, size.Height - padding - blockH - 60)));

            int y = textTopY;
            layout.TitleRect  = new RectangleF(textX, y, textWidth, titleH); y += titleH + 5;
            layout.ArtistRect = new RectangleF(textX, y, textWidth, infoH);  y += infoH  + 5;
            layout.AlbumRect  = new RectangleF(textX, y, textWidth, infoH);

            int btnSize    = 48;
            int btnSpacing = 10;
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

                    Brush textBrush = _currentWidgetBackgroundColor.GetBrightness() > 0.65f
                        ? Brushes.Black : Brushes.White;

                    // Album art
                    if (_currentAlbumArt != null)
                    {
                        g.DrawImage(_currentAlbumArt, layout.AlbumArtRect);
                    }
                    else
                    {
                        using (var placeholder = new SolidBrush(Color.FromArgb(100, 128, 128, 128)))
                            g.FillRectangle(placeholder, layout.AlbumArtRect);

                        using (var sf = new StringFormat
                            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("No Art", _infoFont, textBrush, layout.AlbumArtRect, sf);
                    }

                    // Text
                    if (layout.TitleRect.Width > 0)
                    {
                        g.DrawString(_currentTitle  ?? "Unknown Title",        _titleFont, textBrush, layout.TitleRect,  textFormat);
                        g.DrawString(_currentArtist ?? "Unknown Album Artist",  _infoFont,  textBrush, layout.ArtistRect, textFormat);
                        g.DrawString(_currentAlbum  ?? "Unknown Album",         _infoFont,  textBrush, layout.AlbumRect,  textFormat);
                    }

                    // Buttons
                    if (layout.PrevButtonRect.Width > 0)
                    {
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
    }
}
