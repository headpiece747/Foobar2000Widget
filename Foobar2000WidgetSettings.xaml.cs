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
            // Access WidgetManager through the WidgetObject property of the instance
            if (_widgetInstance.WidgetObject.WidgetManager.LoadSetting(_widgetInstance, "BeefwebApiUrl", out string apiUrl))
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
        private void SaveSettings()
        {
            // Access WidgetManager through the WidgetObject property of the instance
            _widgetInstance.WidgetObject.WidgetManager.StoreSetting(_widgetInstance, "BeefwebApiUrl", BeefwebApiUrlTextBox.Text);
        }

        // Event handler for the Save button click
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            // Optionally, provide feedback to the user that settings have been saved
            MessageBox.Show("Settings saved!", "Foobar2000 Widget", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
