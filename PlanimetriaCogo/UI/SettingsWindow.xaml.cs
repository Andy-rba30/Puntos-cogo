using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PlanimetriaCogo.UI;

/// <summary>Ventana de parámetros. DialogResult true = Aceptar (los valores ya están copiados a Settings).</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        // Fuerza la actualización de las cajas de texto con UpdateSourceTrigger=LostFocus.
        ((Button)sender).Focus();

        var errors = _viewModel.Validate();
        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _viewModel.Apply();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetDefaults();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.ConfigFolder.Length == 0)
                return;
            Directory.CreateDirectory(_viewModel.ConfigFolder);
            Process.Start(new ProcessStartInfo { FileName = _viewModel.ConfigFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo abrir la carpeta: {ex.Message}", "Configuración", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
