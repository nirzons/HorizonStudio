using System;

namespace NirZonshine.NINA.HorizonStudio.ViewModels.Commands {
    public static class RenameDialog {
        public static string Show(string defaultText, string title) {
            var dialog = new System.Windows.Window {
                Title = title,
                Width = 320,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = System.Windows.Application.Current.MainWindow,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0f0f12")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A30")),
                BorderThickness = new System.Windows.Thickness(1),
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStyle = System.Windows.WindowStyle.ToolWindow
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(15) };
            
            var label = new System.Windows.Controls.TextBlock {
                Text = "Enter landmark name:",
                Foreground = System.Windows.Media.Brushes.DarkGray,
                FontSize = 11,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox {
                Text = defaultText,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E24")),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3D3D45")),
                Foreground = System.Windows.Media.Brushes.White,
                Padding = new System.Windows.Thickness(4, 2, 4, 2),
                CaretBrush = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(0, 0, 0, 12)
            };
            textBox.SelectAll();
            stack.Children.Add(textBox);

            var buttonsGrid = new System.Windows.Controls.Grid();
            buttonsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            buttonsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            var okButton = new System.Windows.Controls.Button {
                Content = "OK",
                IsDefault = true,
                Height = 24,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(0, 0, 4, 0)
            };
            okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
            System.Windows.Controls.Grid.SetColumn(okButton, 0);
            buttonsGrid.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button {
                Content = "Cancel",
                IsCancel = true,
                Height = 24,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new System.Windows.Thickness(4, 0, 0, 0)
            };
            cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };
            System.Windows.Controls.Grid.SetColumn(cancelButton, 1);
            buttonsGrid.Children.Add(cancelButton);

            stack.Children.Add(buttonsGrid);
            dialog.Content = stack;

            dialog.Loaded += (s, e) => textBox.Focus();

            if (dialog.ShowDialog() == true) {
                return textBox.Text.Trim();
            }
            return null;
        }
    }
}
