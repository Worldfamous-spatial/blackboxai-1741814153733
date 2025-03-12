using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace CADTR.Views
{
    public partial class InputDialog : Window
    {
        public string Input => InputTextBox.Text;
        private static readonly Regex ValidNameRegex = new Regex(@"^[a-zA-Z0-9\-_\s]+$");

        public InputDialog(string prompt = "Enter Polygon Name")
        {
            InitializeComponent();

            // Set window properties
            Owner = Application.Current.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
            // Configure input handling
            InputTextBox.Text = string.Empty;
            InputTextBox.SelectAll();
            InputTextBox.Focus();

            // Add text changed validation
            InputTextBox.TextChanged += (s, e) => ValidateInput();

            // Add key event handling
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && IsInputValid())
                {
                    DialogResult = true;
                    Close();
                }
                else if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };

            // Set initial state
            ValidateInput();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsInputValid())
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool IsInputValid()
        {
            string input = InputTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                ShowError("Name cannot be empty");
                return false;
            }

            if (input.Length > 50)
            {
                ShowError("Name is too long (maximum 50 characters)");
                return false;
            }

            if (!ValidNameRegex.IsMatch(input))
            {
                ShowError("Name can only contain letters, numbers, spaces, hyphens, and underscores");
                return false;
            }

            HideError();
            return true;
        }

        private void ValidateInput()
        {
            IsInputValid();
        }

        private void ShowError(string message)
        {
            ErrorMessage.Text = message;
            ErrorMessage.Visibility = Visibility.Visible;
        }

        private void HideError()
        {
            ErrorMessage.Text = string.Empty;
            ErrorMessage.Visibility = Visibility.Collapsed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            InputTextBox.Focus();
        }
    }
}
