// Foobar2000WidgetInstance.cs
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq; // Keep for Enumerable.Take and Select
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

// Dummy PlaybackState enum and PlayerState classes (if not defined elsewhere)
public enum PlaybackState { stopped, playing, paused }
public class PlayerState { public ActiveItem ActiveItem { get; set; } public PlaybackState PlaybackState { get; set; } }
public class ActiveItem { public string PlaylistId { get; set; } public int Index { get; set; } public System.Collections.Generic.List<string> Columns { get; set; } }
public class GetPlayerStateResponse { public PlayerState Player { get; set; } }


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
        private string _beefwebApiUrl = "http://localhost:8880/api";
        private bool _settingsLoaded;
        private bool _disposed;

        private PlayerState _currentPlayerState;
        private string _currentTrackId = string.Empty;
        private Bitmap _currentAlbumArt;
        private Bitmap _prevIcon, _playIcon, _pauseIcon, _nextIcon;
        private string _currentTitle = "Unknown Title";
        private string _currentArtist = "Unknown Album Artist";
        private string _currentAlbum = "Unknown Album";
        private bool _albumArtLoadAttempted;
        private Color _currentWidgetBackgroundColor = Color.Black;

        public Foobar2000WidgetInstance(IWidgetObject widgetObject, WidgetSize widgetSize, Guid instanceGuid)
        {
            WidgetObject = widgetObject;
            WidgetSize = widgetSize;
            Guid = instanceGuid;

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _updateCts = new CancellationTokenSource();

            LoadControlIcons();
        }

        private void StartBackgroundPolling()
        {
            if (_disposed || (_updateCts != null && !_updateCts.IsCancellationRequested && Task.CompletedTask.IsCompleted))
            {
                // Avoid starting multiple polling loops
            }

            Task.Run(async () =>
            {
                await Task.Delay(100, _updateCts.Token);

                while (!_disposed)
                {
                    try
                    {
                        if (!_settingsLoaded && WidgetManager != null)
                        {
                            LoadSettings();
                            _settingsLoaded = true;
                        }
                        await Task.Delay(2000, _updateCts.Token);
                        if (_updateCts.Token.IsCancellationRequested || _disposed) break;
                        await UpdateWidgetAsync(_updateCts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Polling error: {ex.Message}");
                        await Task.Delay(5000, CancellationToken.None);
                    }
                }
            }, _updateCts.Token);
        }

        public void RequestUpdate()
        {
            if (WidgetManager != null && !_settingsLoaded)
            {
                LoadSettings();
                _settingsLoaded = true;
            }

            if (_updateCts == null || _updateCts.IsCancellationRequested)
            {
                _updateCts?.Dispose();
                _updateCts = new CancellationTokenSource();
            }

            var token = _updateCts.Token;
            Task.Run(async () => await UpdateWidgetAsync(token), token);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _updateCts?.Cancel();
                    _updateCts?.Dispose();
                    _httpClient?.Dispose();
                    _currentAlbumArt?.Dispose();
                    _prevIcon?.Dispose();
                    _playIcon?.Dispose();
                    _pauseIcon?.Dispose();
                    _nextIcon?.Dispose();
                }
                _prevIcon = _playIcon = _pauseIcon = _nextIcon = null;
                _currentAlbumArt = null;
                _currentPlayerState = null;
                _disposed = true;
                WidgetManager?.WriteLogMessage(this, LogLevel.INFO, "Widget disposed, background polling stopped.");
            }
        }

        public void EnterSleep()
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts = new CancellationTokenSource();
            WidgetManager?.WriteLogMessage(this, LogLevel.INFO, "Entering sleep state. Polling paused.");
        }

        public void ExitSleep()
        {
            WidgetManager?.WriteLogMessage(this, LogLevel.INFO, "Exiting sleep state. Resuming updates.");
            if (_disposed) return;

            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts = new CancellationTokenSource();

            StartBackgroundPolling();
            RequestUpdate();
        }

        public UserControl GetSettingsControl()
        {
            return WidgetManager != null ? new Foobar2000WidgetSettings(this) : null;
        }

        private async Task SendPlayerCommandAsync(string command, string label)
        {
            if (_disposed || string.IsNullOrEmpty(_beefwebApiUrl) || (_updateCts != null && _updateCts.Token.IsCancellationRequested)) return;

            try
            {
                string url = $"{_beefwebApiUrl}/player/{command}";
                using (var response = await _httpClient.PostAsync(url, null, _updateCts.Token))
                {
                    response.EnsureSuccessStatusCode();
                    await Task.Delay(300, _updateCts.Token);
                    await UpdateWidgetAsync(_updateCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.DEBUG, $"{label} command cancelled.");
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"{label} command failed: {ex.Message}");
            }
        }

        private void LoadControlIcons()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string baseNamespace = GetType().Namespace?.Split('.')[0] ?? "Foobar2000Widget";

            _prevIcon = LoadBitmapResource(assembly, $"{baseNamespace}.Resources.prev.png");
            _playIcon = LoadBitmapResource(assembly, $"{baseNamespace}.Resources.play.png");
            _pauseIcon = LoadBitmapResource(assembly, $"{baseNamespace}.Resources.pause.png");
            _nextIcon = LoadBitmapResource(assembly, $"{baseNamespace}.Resources.next.png");

            if (_prevIcon == null || _playIcon == null || _pauseIcon == null || _nextIcon == null)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.ERROR, "One or more control icons failed to load. Check resource paths and names.");
            }
        }

        private Bitmap LoadBitmapResource(Assembly assembly, string resourceName)
        {
            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null) { return new Bitmap(stream); }
                    else
                    {
                        WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Resource stream not found: {resourceName}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Failed to load icon resource '{resourceName}': {ex.Message}");
                return null;
            }
        }

        private void LoadSettings()
        {
            string defaultUrl = "http://localhost:8880/api";
            _beefwebApiUrl = defaultUrl;

            if (WidgetManager != null)
            {
                WidgetManager.WriteLogMessage(this, LogLevel.DEBUG, "Attempting to load setting 'BeefwebApiUrl'.");
                string valueFromSettings;
                if (WidgetManager.LoadSetting(this, "BeefwebApiUrl", out valueFromSettings) && !string.IsNullOrWhiteSpace(valueFromSettings))
                {
                    _beefwebApiUrl = valueFromSettings;
                    WidgetManager.WriteLogMessage(this, LogLevel.DEBUG, $"Successfully loaded 'BeefwebApiUrl': {valueFromSettings}");
                }
                else
                {
                    WidgetManager.WriteLogMessage(this, LogLevel.INFO, $"Could not load 'BeefwebApiUrl' or it was empty. Using default: {defaultUrl}.");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Foobar2000WidgetInstance: WidgetManager is null during LoadSettings. API URL defaults to " + _beefwebApiUrl);
            }
            _beefwebApiUrl = _beefwebApiUrl.Trim().TrimEnd('/');
            WidgetManager?.WriteLogMessage(this, LogLevel.INFO, $"API URL configured to: {_beefwebApiUrl}");
        }

        private async Task UpdateWidgetAsync(CancellationToken cancellationToken)
        {
            if (_disposed || cancellationToken.IsCancellationRequested) return;

            if (string.IsNullOrEmpty(_beefwebApiUrl))
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, "Beefweb API URL is not configured. Cannot update widget.");
                return;
            }

            try
            {
                await GetPlayerStateAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested) return;

                string newTrackId = GenerateTrackIdentifier();
                bool trackChanged = (_currentTrackId != newTrackId);
                _currentTrackId = newTrackId;

                if (trackChanged && _currentPlayerState?.ActiveItem != null)
                {
                    _albumArtLoadAttempted = false;
                    _currentAlbumArt?.Dispose();
                    _currentAlbumArt = null;
                    await GetAlbumArtAsync(_currentPlayerState.ActiveItem.PlaylistId, _currentPlayerState.ActiveItem.Index, cancellationToken);
                }
                else if (_currentAlbumArt == null && !_albumArtLoadAttempted && _currentPlayerState?.ActiveItem != null)
                {
                    await GetAlbumArtAsync(_currentPlayerState.ActiveItem.PlaylistId, _currentPlayerState.ActiveItem.Index, cancellationToken);
                }
                else if (_currentPlayerState?.ActiveItem == null && _currentAlbumArt != null)
                {
                    _currentAlbumArt?.Dispose();
                    _currentAlbumArt = null;
                    _albumArtLoadAttempted = false;
                    UpdateBackgroundColorFromAlbumArt();
                }


                if (cancellationToken.IsCancellationRequested) return;

                using (Bitmap widgetBitmap = DrawWidget())
                {
                    if (widgetBitmap != null)
                    {
                        var eventArgs = new WidgetUpdatedEventArgs
                        {
                            WidgetBitmap = (Bitmap)widgetBitmap.Clone(),
                            Offset = Point.Empty,
                            WaitMax = 1000
                        };
                        WidgetUpdated?.Invoke(this, eventArgs);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.DEBUG, "UpdateWidgetAsync was cancelled.");
            }
            catch (HttpRequestException httpEx)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"HTTP request error updating widget: {httpEx.Message}. Check Foobar2000/Beefweb connection to '{_beefwebApiUrl}'.");
                _currentWidgetBackgroundColor = Color.Black;
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.ERROR, $"Unexpected error updating widget: {ex.ToString()}");
                _currentWidgetBackgroundColor = Color.Black;
            }
        }

        private string GenerateTrackIdentifier()
        {
            if (_currentPlayerState?.ActiveItem == null) return string.Empty;
            var activeItem = _currentPlayerState.ActiveItem;
            var columns = activeItem.Columns;
            string meta = string.Empty;
            if (columns != null && columns.Count > 0)
            {
                meta = string.Join(":", columns.Take(3).Select(c => c ?? string.Empty));
            }
            return $"{activeItem.PlaylistId ?? "N/A"}:{activeItem.Index}:{meta}";
        }

        private async Task GetPlayerStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(_beefwebApiUrl))
                throw new InvalidOperationException("Beefweb API URL is not set.");

            string columnsParam = "%25title%25,%25album artist%25,%25album%25";
            string requestUrl = $"{_beefwebApiUrl}/player?columns={columnsParam}&_={DateTime.Now.Ticks}";

            using (var response = await _httpClient.GetAsync(requestUrl, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                if (cancellationToken.IsCancellationRequested) return;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var playerResponse = JsonSerializer.Deserialize<GetPlayerStateResponse>(json, options);
                _currentPlayerState = playerResponse?.Player;

                if (_currentPlayerState?.ActiveItem?.Columns != null)
                {
                    var cols = _currentPlayerState.ActiveItem.Columns;
                    _currentTitle = cols.Count > 0 ? (cols[0] ?? "Unknown Title") : "Unknown Title";
                    _currentArtist = cols.Count > 1 ? (cols[1] ?? "Unknown Album Artist") : "Unknown Album Artist";
                    _currentAlbum = cols.Count > 2 ? (cols[2] ?? "Unknown Album") : "Unknown Album";
                }
                else if (_currentPlayerState?.ActiveItem == null)
                {
                    _currentTitle = (_currentPlayerState == null || _currentPlayerState.PlaybackState == PlaybackState.stopped) ? "Foobar2000" : "No active track";
                    _currentArtist = string.Empty;
                    _currentAlbum = string.Empty;
                    if (_currentAlbumArt != null)
                    {
                        _currentAlbumArt?.Dispose();
                        _currentAlbumArt = null;
                        UpdateBackgroundColorFromAlbumArt();
                    }
                }
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
                using (Bitmap tinyArt = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(tinyArt))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_currentAlbumArt, new Rectangle(0, 0, 1, 1));
                    _currentWidgetBackgroundColor = tinyArt.GetPixel(0, 0);
                }
                _currentWidgetBackgroundColor = Color.FromArgb(255, _currentWidgetBackgroundColor.R, _currentWidgetBackgroundColor.G, _currentWidgetBackgroundColor.B);
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Error extracting color from album art: {ex.Message}");
                _currentWidgetBackgroundColor = Color.Black;
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

            HttpRequestMessage request = null;
            HttpResponseMessage response = null;
            Stream stream = null;
            MemoryStream ms = null;

            try
            {
                request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Failed to get album art ({playlistId}/{index}): {response.StatusCode}");
                    }
                    _currentAlbumArt?.Dispose();
                    _currentAlbumArt = null;
                    UpdateBackgroundColorFromAlbumArt();
                    return;
                }

                stream = await response.Content.ReadAsStreamAsync();
                if (cancellationToken.IsCancellationRequested) return;
                ms = new MemoryStream();
                await stream.CopyToAsync(ms, 81920, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                _currentAlbumArt?.Dispose();
                if (ms.Length > 0)
                {
                    ms.Position = 0;
                    // By creating a temporary bitmap and then a new bitmap from the temporary one,
                    // we decouple the final bitmap from the memory stream, allowing it to be disposed safely.
                    using (var tempBitmap = new Bitmap(ms))
                    {
                        _currentAlbumArt = new Bitmap(tempBitmap);
                    }
                }
                else { _currentAlbumArt = null; }

                UpdateBackgroundColorFromAlbumArt();
            }
            catch (OperationCanceledException)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.DEBUG, "GetAlbumArtAsync was cancelled.");
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, $"Error getting album art ({playlistId}/{index}): {ex.Message}");
                _currentAlbumArt?.Dispose();
                _currentAlbumArt = null;
                UpdateBackgroundColorFromAlbumArt();
            }
            finally
            {
                ms?.Dispose();
                stream?.Dispose();
                response?.Dispose();
                request?.Dispose();
            }
        }

        public void ClickEvent(ClickType click_type, int x, int y)
        {
            if (click_type != ClickType.Single || _disposed) return;

            Size widgetActualSize = WidgetSize.ToSize();
            if (widgetActualSize.Width <= 0 || widgetActualSize.Height <= 0) return;

            Font titleFont = null; // Not used for button positioning here, but kept for consistency
            Font infoFont = null;  // Not used for button positioning here, but kept for consistency

            try
            {
                // titleFont and infoFont are initialized but not directly used for button layout calculations in this specific block.
                // Their presence might be relevant if text layout influenced button positions more directly.
                titleFont = new Font("Arial", 24, FontStyle.Bold);
                infoFont = new Font("Arial", 20);

                int padding = 10;

                int availableWidthForArt = widgetActualSize.Width - (2 * padding);
                int availableHeightForArt = widgetActualSize.Height - (2 * padding);
                int albumArtSideClick = Math.Min(availableWidthForArt, availableHeightForArt);
                albumArtSideClick = Math.Max(0, albumArtSideClick);

                Rectangle albumRectClick = new Rectangle(padding, padding, albumArtSideClick, albumArtSideClick);

                int textXClick = albumRectClick.Right + padding;
                int textWidthClick = widgetActualSize.Width - textXClick - padding;
                textWidthClick = Math.Max(0, textWidthClick);

                if (textWidthClick > 0)
                {
                    int btnIconSize = 48;
                    int btnSpacing = 10;

                    int buttonsTopClickY;
                    int gapAboveBottomPadding = 15;

                    buttonsTopClickY = widgetActualSize.Height - padding - btnIconSize - gapAboveBottomPadding;
                    buttonsTopClickY = Math.Max(padding, buttonsTopClickY);

                    // MODIFIED: Center the buttons horizontally
                    int totalButtonsWidthNeeded = (3 * btnIconSize) + (2 * btnSpacing);
                    int buttonsStartClickX = textXClick; // Default to left of text area
                    if (totalButtonsWidthNeeded < textWidthClick)
                    {
                        // Center within the available textWidthClick area
                        buttonsStartClickX = textXClick + (textWidthClick - totalButtonsWidthNeeded) / 2;
                    }
                    // Ensure buttons don't go left of the designated text area start or global padding
                    buttonsStartClickX = Math.Max(textXClick, buttonsStartClickX);
                    buttonsStartClickX = Math.Max(padding, buttonsStartClickX);


                    Rectangle prevRect = new Rectangle(buttonsStartClickX, buttonsTopClickY, btnIconSize, btnIconSize);
                    Rectangle playPauseRect = new Rectangle(prevRect.Right + btnSpacing, buttonsTopClickY, btnIconSize, btnIconSize);
                    Rectangle nextRect = new Rectangle(playPauseRect.Right + btnSpacing, buttonsTopClickY, btnIconSize, btnIconSize);

                    Point click = new Point(x, y);
                    if (prevRect.Contains(click)) _ = SendPlayerCommandAsync("previous", "Previous");
                    else if (playPauseRect.Contains(click)) _ = SendPlayerCommandAsync("play-pause", "Play/Pause");
                    else if (nextRect.Contains(click)) _ = SendPlayerCommandAsync("next", "Next");
                }
            }
            finally
            {
                titleFont?.Dispose();
                infoFont?.Dispose();
            }
        }

        private Bitmap DrawWidget()
        {
            Size size = WidgetSize.ToSize();
            if (size.Width <= 0 || size.Height <= 0)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.WARN, "DrawWidget called with invalid size.");
                return null;
            }
            Bitmap bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            Font titleFont = null;
            Font infoFont = null;
            StringFormat sfNoArt = null;
            StringFormat textEllipsisFormat = null; // MODIFIED: Declare StringFormat for ellipsis

            try
            {
                titleFont = new Font("Arial", 24, FontStyle.Bold);
                infoFont = new Font("Arial", 20);
                // MODIFIED: Initialize StringFormat for ellipsis
                textEllipsisFormat = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };


                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    g.Clear(_currentWidgetBackgroundColor);

                    Brush textBrushWhite = Brushes.White;
                    float brightness = _currentWidgetBackgroundColor.GetBrightness();
                    if (brightness > 0.65)
                    {
                        textBrushWhite = Brushes.Black;
                    }

                    int padding = 10;

                    int availableWidthForArt = size.Width - (2 * padding);
                    int availableHeightForArt = size.Height - (2 * padding);
                    int albumArtSide = Math.Min(availableWidthForArt, availableHeightForArt);
                    albumArtSide = Math.Max(0, albumArtSide);

                    Rectangle albumRect = new Rectangle(padding, padding, albumArtSide, albumArtSide);

                    if (_currentAlbumArt != null)
                    {
                        if (albumRect.Width > 0 && albumRect.Height > 0)
                            g.DrawImage(_currentAlbumArt, albumRect);
                    }
                    else
                    {
                        if (albumRect.Width > 0 && albumRect.Height > 0)
                        {
                            using (SolidBrush placeholderBrush = new SolidBrush(Color.FromArgb(100, 128, 128, 128)))
                            {
                                g.FillRectangle(placeholderBrush, albumRect);
                            }
                            sfNoArt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                            g.DrawString("No Art", infoFont, textBrushWhite, albumRect, sfNoArt);
                        }
                    }

                    int textX = albumRect.Right + padding;
                    int textWidth = size.Width - textX - padding;
                    textWidth = Math.Max(0, textWidth);

                    if (textWidth > 0)
                    {
                        int titleFontHeight = titleFont.Height;
                        int infoFontHeight = infoFont.Height;
                        int textBlockHeight = titleFontHeight + infoFontHeight * 2 + 10; // Approximate height for 3 lines of text with some spacing

                        int albumCenterY = albumRect.Top + albumRect.Height / 2;
                        int textBlockTopY = albumCenterY - textBlockHeight / 2;
                        // Ensure textBlockTopY doesn't go above the top padding, or too low if album art is very small
                        textBlockTopY = Math.Max(textBlockTopY, padding);
                        // Ensure the text block doesn't start so low that it would certainly overflow, especially with controls below.
                        // This is a heuristic; actual available height for text depends on button placement too.
                        int estimatedMaxTextTopY = size.Height - padding - textBlockHeight - 60; // 60 is a rough estimate for buttons area
                        textBlockTopY = Math.Min(textBlockTopY, Math.Max(padding, estimatedMaxTextTopY));


                        int currentTextY = textBlockTopY;
                        // MODIFIED: Use textEllipsisFormat for drawing strings
                        g.DrawString(_currentTitle ?? "Unknown Title", titleFont, textBrushWhite, new RectangleF(textX, currentTextY, textWidth, titleFontHeight), textEllipsisFormat);
                        currentTextY += titleFontHeight + 5;
                        g.DrawString(_currentArtist ?? "Unknown Album Artist", infoFont, textBrushWhite, new RectangleF(textX, currentTextY, textWidth, infoFontHeight), textEllipsisFormat);
                        currentTextY += infoFontHeight + 5;
                        g.DrawString(_currentAlbum ?? "Unknown Album", infoFont, textBrushWhite, new RectangleF(textX, currentTextY, textWidth, infoFontHeight), textEllipsisFormat);

                        int btnIconSize = 48;
                        int btnSpacing = 10;

                        int buttonsTopDrawY;
                        int gapAboveBottomPadding = 15;

                        buttonsTopDrawY = size.Height - padding - btnIconSize - gapAboveBottomPadding;
                        buttonsTopDrawY = Math.Max(padding, buttonsTopDrawY);

                        // MODIFIED: Center the buttons horizontally
                        int totalButtonsWidthNeeded = (3 * btnIconSize) + (2 * btnSpacing);
                        int buttonsStartDrawX = textX; // Default to left of text area
                        if (totalButtonsWidthNeeded < textWidth)
                        {
                            // Center within the available textWidth area
                            buttonsStartDrawX = textX + (textWidth - totalButtonsWidthNeeded) / 2;
                        }
                        // Ensure buttons don't go left of the designated text area start or global padding
                        buttonsStartDrawX = Math.Max(textX, buttonsStartDrawX);
                        buttonsStartDrawX = Math.Max(padding, buttonsStartDrawX);


                        Rectangle prevRect = new Rectangle(buttonsStartDrawX, buttonsTopDrawY, btnIconSize, btnIconSize);
                        Rectangle playPauseRect = new Rectangle(prevRect.Right + btnSpacing, buttonsTopDrawY, btnIconSize, btnIconSize);
                        Rectangle nextRect = new Rectangle(playPauseRect.Right + btnSpacing, buttonsTopDrawY, btnIconSize, btnIconSize);

                        if (_prevIcon != null && prevRect.Width > 0 && prevRect.Height > 0 && prevRect.Right <= size.Width - padding) g.DrawImage(_prevIcon, prevRect);
                        Bitmap currentPlayPauseIcon = (_currentPlayerState?.PlaybackState == PlaybackState.playing ? _pauseIcon : _playIcon);
                        if (currentPlayPauseIcon != null && playPauseRect.Width > 0 && playPauseRect.Height > 0 && playPauseRect.Right <= size.Width - padding) g.DrawImage(currentPlayPauseIcon, playPauseRect);
                        if (_nextIcon != null && nextRect.Width > 0 && nextRect.Height > 0 && nextRect.Right <= size.Width - padding) g.DrawImage(_nextIcon, nextRect);
                    }
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                WidgetManager?.WriteLogMessage(this, LogLevel.ERROR, $"Error during DrawWidget: {ex.ToString()}");
                bitmap?.Dispose();
                return null;
            }
            finally
            {
                titleFont?.Dispose();
                infoFont?.Dispose();
                sfNoArt?.Dispose();
                textEllipsisFormat?.Dispose(); // MODIFIED: Dispose of StringFormat
            }
        }
    }

    public partial class Foobar2000WidgetSettings : UserControl
    {
        public Foobar2000WidgetSettings(Foobar2000WidgetInstance instance) { /* ... */ }
    }

} // End of namespace Foobar2000Widget