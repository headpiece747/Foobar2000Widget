// Foobar2000WidgetSettings.xaml.cs
using System.Windows; // Required for RoutedEventArgs, MessageBox, etc.
using System.Windows.Controls;
using WigiDashWidgetFramework; // Assuming IWidgetInstance is accessible

namespace Foobar2000Widget
{
    /// <summary>
    /// Interaction logic for Foobar2000WidgetSettings.xaml
    /// </summary>
    public partial class Foobar2000WidgetSettings : UserControl
    {
        private IWidgetInstance _widgetInstance;

        // Constructor
        public Foobar2000WidgetSettings(IWidgetInstance widgetInstance)
        {
            InitializeComponent(); // This method is generated from the XAML file

            _widgetInstance = widgetInstance;

            // Load the current setting when the control is initialized
            LoadSettings();
        }

        // Loads settings from the widget instance
        private void LoadSettings()
        {
            var manager = _widgetInstance?.WidgetObject?.WidgetManager;
            if (manager != null && manager.LoadSetting(_widgetInstance, "BeefwebApiUrl", out string apiUrl) && !string.IsNullOrWhiteSpace(apiUrl))
            {
                BeefwebApiUrlTextBox.Text = apiUrl;
            }
            else
            {
                // Set a default value if the setting is not found
                BeefwebApiUrlTextBox.Text = "http://localhost:8880/api";
            }
        }

        // Saves settings to the widget instance
        private bool SaveSettings()
        {
            var manager = _widgetInstance?.WidgetObject?.WidgetManager;
            if (manager == null) return false;

            string inputUrl = BeefwebApiUrlTextBox.Text?.Trim()?.TrimEnd('/') ?? "";
            if (!System.Uri.TryCreate(inputUrl, System.UriKind.Absolute, out System.Uri uriResult) ||
                (uriResult.Scheme != System.Uri.UriSchemeHttp && uriResult.Scheme != System.Uri.UriSchemeHttps))
            {
                MessageBox.Show("Please enter a valid HTTP or HTTPS API URL (e.g., http://localhost:8880/api).", "Invalid API URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            manager.StoreSetting(_widgetInstance, "BeefwebApiUrl", inputUrl);
            return true;
        }

        // Event handler for the Save button click
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveSettings())
            {
                MessageBox.Show("Settings saved!", "Foobar2000 Widget", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
