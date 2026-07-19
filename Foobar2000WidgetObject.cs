using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace Foobar2000Widget
{
    public class Foobar2000WidgetObject : IWidgetObject
    {
        private const string PREVIEW_RESOURCE = "Foobar2000Widget.Resources.preview.png";

        public Guid       Guid        => new Guid("{45036078-FE39-4A51-9C01-D7DA1677686B}");
        public string     Name        => "foobar2000 Player";
        public string     Author      => "headpiece747";
        public string     Website     => "https://eclipticsight.com";
        public string     Description => "Control foobar2000";

        private Version _cachedVersion;
        private readonly object _previewLock = new object();

        // Assembly.GetName().Version is always 4-part (e.g. 1.0.1.0).
        // We trim trailing ".0" segments so the display matches the 3-part
        // version set in AssemblyInfo.cs (e.g. 1.0.1).
        public Version Version
        {
            get
            {
                if (_cachedVersion != null) return _cachedVersion;
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v == null) return _cachedVersion = new Version(1, 0, 0);

                // Build trimmed string: drop revision if 0, then drop build if also 0
                if (v.Revision != 0)
                    return _cachedVersion = new Version(v.Major, v.Minor, v.Build, v.Revision);
                if (v.Build != 0)
                    return _cachedVersion = new Version(v.Major, v.Minor, v.Build);
                return _cachedVersion = new Version(v.Major, v.Minor);
            }
        }

        public SdkVersion TargetSdk   => WidgetUtility.CurrentSdkVersion;

        public List<WidgetSize> SupportedSizes => new List<WidgetSize> { new WidgetSize(5, 4) };
        public IWidgetManager WidgetManager    { get; set; }
        public string         LastErrorMessage { get; set; }

        private Bitmap _previewImage;

        // All three properties share the same cached instance
        public Bitmap PreviewImage                   => EnsurePreview();
        public Bitmap WidgetThumbnail                => EnsurePreview();
        public Bitmap GetWidgetPreview(WidgetSize _) => EnsurePreview();

        private Bitmap EnsurePreview()
        {
            if (_previewImage != null) return _previewImage;

            lock (_previewLock)
            {
                if (_previewImage != null) return _previewImage;

                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    using (var stream = assembly.GetManifestResourceStream(PREVIEW_RESOURCE))
                    {
                        if (stream != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                stream.CopyTo(ms);
                                ms.Position  = 0;
                                using (var temp = new Bitmap(ms))
                                {
                                    _previewImage = new Bitmap(temp);
                                }
                                return _previewImage;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"Error loading preview: {ex.Message}";
                }

                _previewImage = CreatePlaceholderBitmap();
                return _previewImage;
            }
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widgetSize, Guid instanceGuid)
            => new Foobar2000WidgetInstance(this, widgetSize, instanceGuid);

        public bool RemoveWidgetInstance(Guid instanceGuid) => true;

        public WidgetError Load(string resourcePath)
        {
            lock (_previewLock)
            {
                _previewImage?.Dispose();
                _previewImage = null;
            }
            EnsurePreview();
            return WidgetError.NO_ERROR;
        }

        public WidgetError Unload()
        {
            lock (_previewLock)
            {
                _previewImage?.Dispose();
                _previewImage = null;
            }
            return WidgetError.NO_ERROR;
        }

        private Bitmap CreatePlaceholderBitmap()
        {
            var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Arial", 12, System.Drawing.FontStyle.Bold))
            {
                g.Clear(Color.DarkGray);
                g.DrawString("?", f, Brushes.White, 5, 5);
            }
            return bmp;
        }
    }
}
