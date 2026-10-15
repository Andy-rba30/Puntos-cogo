using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PlanimetriaCogo.UI;

/// <summary>Ventana de clasificación de descripciones (§10.2). DialogResult true = Dibujar.</summary>
public partial class ClassificationWindow : Window
{
    private readonly ClassificationViewModel _viewModel;

    // Edición en bloque: fila, propiedad, valor previo y filas seleccionadas al empezar a editar la celda.
    private ClassificationRowViewModel? _editingRow;
    private string? _editingProperty;
    private object? _editingOldValue;
    private List<ClassificationRowViewModel> _editingSelection = new();

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

    // ---- Edición en bloque ----
    // Si hay varias filas marcadas en Sel. (o seleccionadas en la tabla) y se edita una de ellas, el cambio se
    // copia a las demás. Se guarda el valor previo al empezar y se compara al confirmar, así una edición que no
    // cambió nada (abrir la celda y salir) no toca al resto.

    private void RowsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        _editingRow = e.Row.Item as ClassificationRowViewModel;
        _editingProperty = e.Column.SortMemberPath;
        _editingOldValue = _editingRow != null && !string.IsNullOrEmpty(_editingProperty)
            ? _editingRow.GetEditableValue(_editingProperty)
            : null;
        // Se toma aquí: el clic que confirma la edición puede cambiar la selección antes de CellEditEnding.
        _editingSelection = RowsGrid.SelectedItems.OfType<ClassificationRowViewModel>().ToList();
    }

    private void RowsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        var row = _editingRow;
        var property = _editingProperty;
        var oldValue = _editingOldValue;
        var selection = _editingSelection;
        _editingRow = null;
        _editingProperty = null;
        _editingOldValue = null;
        _editingSelection = new List<ClassificationRowViewModel>();

        if (e.EditAction != DataGridEditAction.Commit || row == null || string.IsNullOrEmpty(property))
            return;
        if (!ReferenceEquals(e.Row.Item, row))
            return;

        // El enlace de la celda se confirma después de este evento: propagar cuando el nuevo valor ya esté en la fila.
        Dispatcher.InvokeAsync(() =>
        {
            if (Equals(row.GetEditableValue(property), oldValue))
                return;
            _viewModel.PropagateEdit(row, property, selection);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Un clic simple sobre una fila que ya forma parte de una selección múltiple la dejaría sola (comportamiento
    /// del DataGrid). Aquí se conserva la selección y se abre la celda directamente en edición, para que el cambio
    /// se aplique a todas las filas seleccionadas. Con Ctrl o Mayús se deja actuar al DataGrid.
    /// </summary>
    private void RowsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || RowsGrid.SelectedItems.Count < 2)
            return;
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell == null || cell.IsEditing)
            return;
        if (cell.DataContext is not ClassificationRowViewModel row || !RowsGrid.SelectedItems.Contains(row))
            return;

        e.Handled = true;
        if (!cell.IsKeyboardFocusWithin)
            cell.Focus();

        switch (cell.Column.SortMemberPath)
        {
            case nameof(ClassificationRowViewModel.IsSelected):
                // Marca Sel. sin perder la selección de la tabla.
                row.IsSelected = !row.IsSelected;
                return;
            case nameof(ClassificationRowViewModel.SplitBySide):
                // La casilla no recibe el clic (se consumió): se abre la edición y se alterna el valor a mano.
                RowsGrid.CurrentCell = new DataGridCellInfo(cell);
                if (RowsGrid.BeginEdit())
                    row.SplitBySide = !row.SplitBySide;
                return;
            default:
                if (cell.IsReadOnly)
                    return;
                RowsGrid.CurrentCell = new DataGridCellInfo(cell);
                RowsGrid.BeginEdit();
                return;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>Confirma la celda en edición para que el valor llegue al view-model antes de actuar.</summary>
    private void CommitPendingEdits()
    {
        RowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
