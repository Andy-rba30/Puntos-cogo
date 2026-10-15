using System.Linq;
using System.Windows;

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

    private void DrawButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
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
        RowsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        RowsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
    }
}
