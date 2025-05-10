// Foobar2000WidgetObject.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace Foobar2000Widget
{
    public class Foobar2000WidgetObject : IWidgetObject
    {
        // --- Constants ---
        // Using lowercase 'preview.png' as requested previously
        private const string PREVIEW_RESOURCE_NAME = "Foobar2000Widget.Resources.preview.png"; // Adjust if your namespace/path differs

        // --- Fields ---
        public Guid Guid => new Guid("{45036078-FE39-4A51-9C01-D7DA1677686B}");
        public string Name => "foobar2000 Player";
        public string Author => "headpiece747";
        public string Website => "https://eclipticsight.com";
        public string Description => "Control foobar2000";
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;
        public SdkVersion TargetSdk => WidgetUtility.CurrentSdkVersion;
        public List<WidgetSize> SupportedSizes => new List<WidgetSize> { new WidgetSize(5, 4) };
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        // Removed _resourcePath as it was only needed for thumb.png
        private Bitmap _previewImage;
        // Removed _thumb field
        private bool _previewLoadAttempted = false;
        // Removed _thumbLoadAttempted flag

        // --- Properties ---
        public Bitmap PreviewImage // Handles loading the embedded preview.png
        {
            get
            {
                Log("Accessing PreviewImage property...");
                if (!_previewLoadAttempted)
                {
                    _previewLoadAttempted = true;
                    Log($"Attempting to load embedded resource: '{PREVIEW_RESOURCE_NAME}'");
                    try
                    {
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        using (var stream = assembly.GetManifestResourceStream(PREVIEW_RESOURCE_NAME))
                        {
                            if (stream != null)
                            {
                                Log("Embedded resource stream found. Loading bitmap...");
                                _previewImage = new Bitmap(stream);
                                Log("Preview bitmap loaded successfully from embedded resource.");
                            }
                            else
                            {
                                Log($"ERROR: Embedded resource stream NOT FOUND for '{PREVIEW_RESOURCE_NAME}'.");
                                LastErrorMessage = $"Embedded resource '{PREVIEW_RESOURCE_NAME}' not found.";
                                _previewImage = CreatePlaceholderBitmap("Preview ERR");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: Failed to load embedded preview image: {ex.Message}");
                        LastErrorMessage = $"Error loading embedded preview: {ex.Message}";
                        _previewImage?.Dispose();
                        _previewImage = CreatePlaceholderBitmap("Preview EXC");
                    }
                }
                else
                {
                    Log("Preview load already attempted. Returning cached result.");
                }
                return _previewImage ?? (_previewImage = CreatePlaceholderBitmap("Preview NULL"));
            }
        }

        // ** CHANGED: WidgetThumbnail now directly returns PreviewImage **
        public Bitmap WidgetThumbnail => PreviewImage; // Use the same image for sidebar/gallery

        public Bitmap GetWidgetPreview(WidgetSize widgetSize) => PreviewImage; // Also uses the preview image

        // --- Methods ---
        public IWidgetInstance CreateWidgetInstance(WidgetSize widgetSize, Guid instanceGuid)
        {
            Log($"Creating instance {instanceGuid} with size {widgetSize}");
            return new Foobar2000WidgetInstance(this, widgetSize, instanceGuid);
        }

        public bool RemoveWidgetInstance(Guid instanceGuid)
        {
            Log($"Removing instance {instanceGuid}");
            return true;
        }

        public WidgetError Load(string resourcePath)
        {
            // Store resourcePath if needed for other things, otherwise can be removed
            // _resourcePath = resourcePath;
            Log($"Load method called. ResourcePath provided: '{resourcePath}' (Not used for images anymore).");
            _previewLoadAttempted = false; // Reset flag on load

            // --- Removed all thumb.png loading code ---

            // --- Trigger Preview Load (if not already accessed) ---
            Log("Triggering PreviewImage property access to ensure it loads...");
            var preview = this.PreviewImage; // Access property to trigger loading logic
            Log($"PreviewImage property accessed. Current state: {(_previewImage == null ? "null" : "loaded/placeholder")}");

            // --- Final Check ---
            // Slightly simplified check: if PreviewImage property returns null after attempted load, something is wrong.
            if (preview == null)
            {
                Log("ERROR: Critical preview image failed to load properly during Load method (result is null).");
                if (string.IsNullOrEmpty(LastErrorMessage)) LastErrorMessage = "Failed to load critical embedded preview image.";
                return WidgetError.UNDEFINED_ERROR; // Indicate failure if Preview is bad
            }
            // Check if it looks like our placeholder due to a logged error
            if (!string.IsNullOrEmpty(LastErrorMessage) && LastErrorMessage.Contains("preview"))
            {
                // Optionally return error, or allow loading with placeholder
                Log("WARN: Preview image might be a placeholder due to loading error.");
                // return WidgetError.UNDEFINED_ERROR; // Uncomment this line if placeholder is unacceptable
            }


            Log("Foobar2000 Widget Load method completed successfully.");
            return WidgetError.NO_ERROR;
        }


        public WidgetError Unload()
        {
            Log("Unload method called.");
            try
            {
                // Removed _thumb?.Dispose();
                _previewImage?.Dispose();
                _previewImage = null;
                Log("Preview bitmap disposed.");
            }
            catch (Exception ex)
            {
                Log($"WARN: Error disposing preview bitmap during Unload: {ex.Message}");
            }
            Log("Foobar2000 Widget Unloaded.");
            return WidgetError.NO_ERROR;
        }

        // --- Helpers ---
        private void Log(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}] [Foobar2000WidgetObject] {message}";
            Console.WriteLine(logMessage);
            // WidgetManager?.LogMessage(LogLevel.Information, message);
        }

        private Bitmap CreatePlaceholderBitmap(string text = "?")
        {
            Log($"Creating placeholder bitmap with text '{text}'.");
            Bitmap placeholder = new Bitmap(64, 64);
            using (Graphics g = Graphics.FromImage(placeholder))
            {
                g.Clear(Color.DarkGray);
                using (Font f = new Font("Arial", 12, FontStyle.Bold))
                {
                    if (!string.IsNullOrEmpty(text) && text != "Preview NULL")
                    {
                        g.DrawString(text, f, Brushes.White, 5, 5);
                    }
                    else
                    {
                        g.DrawString("?", f, Brushes.White, 5, 5);
                    }
                }
            }
            return placeholder;
        }
    }
}