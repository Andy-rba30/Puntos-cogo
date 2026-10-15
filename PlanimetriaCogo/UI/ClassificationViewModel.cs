using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.UI;

/// <summary>
/// Estado de la ventana de clasificación: filas, lista de códigos disponibles y contador en vivo.
/// No depende de AutoCAD, así el flujo puede ejercitarse sin Civil 3D.
/// </summary>
public sealed class ClassificationViewModel : INotifyPropertyChanged
{
    private readonly CodeCatalog _catalog;
    private readonly Settings _settings;
    private string _unifyCode = "";
    private string _statusMessage = "";

    public ClassificationViewModel(IEnumerable<CodeRule> rules, CodeCatalog catalog, Settings settings)
    {
        _catalog = catalog;
        _settings = settings;
        Rows = new ObservableCollection<ClassificationRowViewModel>(
            rules.Select(r => new ClassificationRowViewModel(r, catalog, settings)));
        foreach (var row in Rows)
            row.PropertyChanged += OnRowChanged;
        RefreshAvailableCodes();
        RefreshTotals();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ClassificationRowViewModel> Rows { get; }

    /// <summary>Códigos del catálogo más los destinos ya usados, para los desplegables.</summary>
    public ObservableCollection<string> AvailableCodes { get; } = new();

    public IReadOnlyList<string> KindOptions => CodeKindNames.All;

    /// <summary>Código que se asigna con "Unificar selección".</summary>
    public string UnifyCode
    {
        get => _unifyCode;
        set { if (_unifyCode != value) { _unifyCode = value; OnPropertyChanged(); } }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    // Contador en vivo.
    public int PointsToLines { get; private set; }
    public int PointsToPointObjects { get; private set; }
    public int PointsIgnored { get; private set; }
    public int PointsUnresolved { get; private set; }
    public int TotalPoints { get; private set; }

    public string Summary =>
        $"Puntos → líneas: {PointsToLines}   |   → puntos: {PointsToPointObjects}   |   ignorados: {PointsIgnored}   |   sin resolver: {PointsUnresolved}   |   total: {TotalPoints}";

    public bool CanDraw => PointsToLines > 0;

    /// <summary>Botón "Sugerir similares".</summary>
    public void SuggestSimilar()
    {
        var entries = Rows.Select(r => (r.Rule.Original, r.Rule.Count));
        var groups = new SimilarityGrouper().Suggest(entries, _catalog);
        var map = SimilarityGrouper.ToMap(groups);

        var changed = 0;
        foreach (var row in Rows)
        {
            if (row.Rule.Kind == CodeKind.Ignore)
                continue;
            if (!map.TryGetValue(row.Rule.Original, out var destination) || destination == row.DestinationCode)
                continue;
            row.DestinationCode = destination;
            changed++;
        }

        StatusMessage = changed == 0
            ? "Sin cambios: no se encontraron descripciones similares pendientes."
            : $"Se propusieron {changed} cambio(s) de código destino. Revise y corrija lo que no corresponda.";
    }

    /// <summary>Botón "Unificar selección": asigna UnifyCode a las filas marcadas (o a las indicadas).</summary>
    public void UnifySelection(IEnumerable<ClassificationRowViewModel>? fallbackSelection = null)
    {
        var code = DescriptionNormalizer.Normalize(UnifyCode);
        if (code.Length == 0)
        {
            StatusMessage = "Indique el código destino para unificar.";
            return;
        }

        var targets = Rows.Where(r => r.IsSelected).ToList();
        if (targets.Count == 0 && fallbackSelection != null)
            targets = fallbackSelection.ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Marque al menos una fila para unificar.";
            return;
        }

        foreach (var row in targets)
        {
            row.DestinationCode = code;
            row.IsSelected = false;
        }
        StatusMessage = $"{targets.Count} fila(s) unificadas en {code}.";
    }

    /// <summary>Reglas finales para el motor.</summary>
    public List<CodeRule> ToRules() => Rows.Select(r => r.Rule).ToList();

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClassificationRowViewModel.IsSelected))
            return;
        if (e.PropertyName == nameof(ClassificationRowViewModel.DestinationCode))
            RefreshAvailableCodes();
        RefreshTotals();
    }

    private void RefreshAvailableCodes()
    {
        var codes = _catalog.Codes
            .Concat(Rows.Select(r => r.DestinationCode))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        if (codes.SequenceEqual(AvailableCodes))
            return;
        AvailableCodes.Clear();
        foreach (var code in codes)
            AvailableCodes.Add(code);
    }

    private void RefreshTotals()
    {
        int lines = 0, pointObjects = 0, ignored = 0, unresolved = 0, total = 0;
        foreach (var row in Rows)
        {
            var rule = row.Rule;
            total += rule.Count;
            if (rule.Kind == CodeKind.Ignore)
                ignored += rule.Count;
            else if (!rule.IsResolved)
                unresolved += rule.Count;
            else if (rule.Kind == CodeKind.Point)
                pointObjects += rule.Count;
            else
                lines += rule.Count;
        }

        PointsToLines = lines;
        PointsToPointObjects = pointObjects;
        PointsIgnored = ignored;
        PointsUnresolved = unresolved;
        TotalPoints = total;
        OnPropertyChanged(nameof(PointsToLines));
        OnPropertyChanged(nameof(PointsToPointObjects));
        OnPropertyChanged(nameof(PointsIgnored));
        OnPropertyChanged(nameof(PointsUnresolved));
        OnPropertyChanged(nameof(TotalPoints));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanDraw));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
