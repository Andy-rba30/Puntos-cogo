using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PlanimetriaCogo.UI;

/// <summary>Ventana de clasificación de descripciones (§10.2). DialogResult true = Dibujar.</summary>
public partial class ClassificationWindow : Window
{
    private readonly ClassificationViewModel _viewModel;

    public ClassificationWindow(ClassificationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void SuggestButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        _viewModel.SuggestSimilar();
    }

    private void UnifyButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        _viewModel.UnifySelection(RowsGrid.SelectedItems.OfType<ClassificationRowViewModel>());
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        var answer = MessageBox.Show(this, "Se descartarán todos los cambios de la tabla y volverá la propuesta inicial del catálogo.\n¿Desea continuar?",
            "Restablecer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            _viewModel.ResetToInitial();
    }

    private void LoadProfileButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        var name = ProfileCombo.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Elija o escriba el nombre del perfil a cargar.", "Perfil", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _viewModel.LoadProfile(name);
    }

    private void LoadProfileFileButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        var dialog = new OpenFileDialog
        {
            Title = "Cargar perfil de clasificación",
            Filter = "Perfiles JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            _viewModel.LoadProfile(dialog.FileName);
    }

    private void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        var name = ProfileCombo.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Escriba un nombre para el perfil (por ejemplo, el nombre del proyecto).", "Perfil", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_viewModel.ProfileNames.Contains(name))
        {
            var answer = MessageBox.Show(this, $"El perfil '{name}' ya existe. ¿Desea reemplazarlo?", "Perfil", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }
        if (_viewModel.SaveProfile(name) != null)
            ProfileCombo.Text = name;
    }

    private void AddToCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        _viewModel.AddToCatalog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        var settingsViewModel = new SettingsViewModel(_viewModel.Settings, Config.ConfigPaths.Default);
        var window = new SettingsWindow(settingsViewModel) { Owner = this };
        if (window.ShowDialog() == true)
            _viewModel.RefreshSettingsSummary();
    }

    private void DrawButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        if (!_viewModel.CanDraw)
            return;

        if (_viewModel.PointsUnresolved > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"Hay {_viewModel.PointsUnresolved} punto(s) sin código destino que no se dibujarán.\n¿Desea continuar?",
                "Puntos sin resolver",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        if (_viewModel.UpdateDescriptionsInDrawing || _viewModel.MovePointObjectsToLayer)
        {
            var actions = string.Join(" y ", new[]
            {
                _viewModel.UpdateDescriptionsInDrawing ? "reescribirá las descripciones de los puntos resueltos" : null,
                _viewModel.MovePointObjectsToLayer ? "moverá los objetos puntuales a su capa" : null
            }.Where(s => s != null));
            var answer = MessageBox.Show(
                this,
                $"El comando {actions}. Un Deshacer revierte todo el comando.\n¿Desea continuar?",
                "Modificar puntos del dibujo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Confirma la celda en edición para que el valor llegue al view-model antes de actuar.</summary>
    private void CommitPendingEdits()
    {
        RowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
