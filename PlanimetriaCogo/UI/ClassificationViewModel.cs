using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.UI;

/// <summary>
/// Estado de la ventana de clasificación: filas, filtro, lista de códigos disponibles, contador en vivo,
/// perfiles, catálogo y opciones de dibujo. No depende de AutoCAD ni de WPF, así el flujo puede
/// ejercitarse sin Civil 3D.
/// </summary>
public sealed class ClassificationViewModel : INotifyPropertyChanged
{
    private CodeCatalog _catalog;
    private readonly Settings _settings;
    private readonly ProfileStore? _profiles;
    private readonly ConfigService? _config;
    private readonly List<CodeRule> _initialRules;
    private string _unifyCode = "";
    private string _statusMessage = "";
    private string _filterText = "";
    private bool _showOnlyUnresolved;
    private bool _axisFromPoints;

    public ClassificationViewModel(IEnumerable<CodeRule> rules, CodeCatalog catalog, Settings settings)
        : this(rules, catalog, settings, null, null)
    {
    }

    public ClassificationViewModel(IEnumerable<CodeRule> rules, CodeCatalog catalog, Settings settings, ProfileStore? profiles, ConfigService? config)
    {
        _catalog = catalog;
        _settings = settings;
        _profiles = profiles;
        _config = config;
        var list = rules.ToList();
        _initialRules = list.Select(r => r.Clone()).ToList();
        Rows = new ObservableCollection<ClassificationRowViewModel>(
            list.Select(r => new ClassificationRowViewModel(r, catalog, settings)));
        foreach (var row in Rows)
            row.PropertyChanged += OnRowChanged;
        RefreshAvailableCodes();
        RefreshVisibleRows();
        RefreshTotals();
        RefreshProfileNames();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Settings Settings => _settings;

    public CodeCatalog Catalog => _catalog;

    /// <summary>Todas las filas.</summary>
    public ObservableCollection<ClassificationRowViewModel> Rows { get; }

    /// <summary>Filas que pasan el filtro (lo que muestra la tabla).</summary>
    public ObservableCollection<ClassificationRowViewModel> VisibleRows { get; } = new();

    /// <summary>Códigos del catálogo más los destinos ya usados, para los desplegables.</summary>
    public ObservableCollection<string> AvailableCodes { get; } = new();

    /// <summary>Nombres de perfiles guardados.</summary>
    public ObservableCollection<string> ProfileNames { get; } = new();

    public IReadOnlyList<string> KindOptions => CodeKindNames.All;

    public bool HasProfiles => _profiles != null;

    public bool HasCatalogStore => _config != null;

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

    /// <summary>Texto del filtro: coincide con descripción original, código destino, capa o estado.</summary>
    public string FilterText
    {
        get => _filterText;
        set { if (_filterText != value) { _filterText = value ?? ""; OnPropertyChanged(); RefreshVisibleRows(); } }
    }

    public bool ShowOnlyUnresolved
    {
        get => _showOnlyUnresolved;
        set { if (_showOnlyUnresolved != value) { _showOnlyUnresolved = value; OnPropertyChanged(); RefreshVisibleRows(); } }
    }

    /// <summary>true cuando el eje se construirá desde los puntos con código <see cref="Settings.AxisCode"/>.</summary>
    public bool AxisFromPoints
    {
        get => _axisFromPoints;
        set { if (_axisFromPoints != value) { _axisFromPoints = value; OnPropertyChanged(); RefreshTotals(); } }
    }

    // ---- Opciones de dibujo (se guardan en config.json al pulsar Dibujar) ----

    public bool UpdateDescriptionsInDrawing
    {
        get => _settings.UpdateDescriptionsInDrawing;
        set { if (_settings.UpdateDescriptionsInDrawing != value) { _settings.UpdateDescriptionsInDrawing = value; OnPropertyChanged(); } }
    }

    public bool MovePointObjectsToLayer
    {
        get => _settings.MovePointObjectsToLayer;
        set { if (_settings.MovePointObjectsToLayer != value) { _settings.MovePointObjectsToLayer = value; OnPropertyChanged(); } }
    }

    public bool ReportToFile
    {
        get => _settings.ReportToFile;
        set { if (_settings.ReportToFile != value) { _settings.ReportToFile = value; OnPropertyChanged(); } }
    }

    public bool KeepGeneratedAxis
    {
        get => _settings.KeepGeneratedAxis;
        set { if (_settings.KeepGeneratedAxis != value) { _settings.KeepGeneratedAxis = value; OnPropertyChanged(); } }
    }

    /// <summary>Resumen de los parámetros vigentes (se actualiza al cerrar la ventana de parámetros).</summary>
    public string SettingsSummary =>
        $"offsetMax {_settings.OffsetMax:0.##} m · distMax {Limit(_settings.DistMax)} · salto PK {Limit(_settings.StationGapMax)} · " +
        $"tolEje {_settings.AxisTolerance:0.###} m · marcas {(_settings.UseDescriptionMarks ? "sí" : "no")} · series {(_settings.SplitSeries ? "sí" : "no")} · " +
        $"orden {(_settings.Order == PointOrder.Station ? "progresiva" : "número")}";

    private static string Limit(double value) => value > 0 ? $"{value:0.##} m" : "sin límite";

    public void RefreshSettingsSummary()
    {
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(UpdateDescriptionsInDrawing));
        OnPropertyChanged(nameof(MovePointObjectsToLayer));
        OnPropertyChanged(nameof(ReportToFile));
        OnPropertyChanged(nameof(KeepGeneratedAxis));
        RefreshTotals();
    }

    // ---- Contador en vivo ----

    public int PointsToLines { get; private set; }
    public int PointsToPointObjects { get; private set; }
    public int PointsIgnored { get; private set; }
    public int PointsUnresolved { get; private set; }
    public int TotalPoints { get; private set; }

    /// <summary>Puntos cuyo destino es el código del eje (para "eje desde puntos").</summary>
    public int AxisPointCount { get; private set; }

    public string Summary =>
        $"Puntos → líneas: {PointsToLines}   |   → puntos: {PointsToPointObjects}   |   ignorados: {PointsIgnored}   |   sin resolver: {PointsUnresolved}   |   total: {TotalPoints}"
        + (AxisFromPoints ? $"   |   eje ({_settings.AxisCode}): {AxisPointCount} puntos" : "");

    public bool CanDraw => PointsToLines > 0 && (!AxisFromPoints || AxisPointCount >= 2);

    /// <summary>Motivo por el que no se puede dibujar, o vacío.</summary>
    public string CannotDrawReason =>
        PointsToLines == 0 ? "No hay puntos que vayan a líneas."
        : AxisFromPoints && AxisPointCount < 2 ? $"Se necesitan al menos dos puntos con código {_settings.AxisCode} para construir el eje."
        : "";

    // ---- Acciones ----

    /// <summary>Botón "Sugerir similares".</summary>
    public void SuggestSimilar()
    {
        var entries = Rows.Select(r => (r.Rule.Original, r.Rule.Count));
        var groups = new SimilarityGrouper { Parser = new DescriptionParser(_settings) }.Suggest(entries, _catalog);
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

    /// <summary>
    /// Edición en bloque: tras editar una propiedad en <paramref name="source"/>, copia el nuevo valor a las demás
    /// filas marcadas en la columna Sel. o seleccionadas en la tabla (<paramref name="gridSelection"/>).
    /// Solo actúa si la fila editada forma parte de ese conjunto: editar una fila suelta no toca a las marcadas.
    /// Devuelve cuántas filas cambiaron.
    /// </summary>
    public int PropagateEdit(ClassificationRowViewModel source, string propertyName, IEnumerable<ClassificationRowViewModel>? gridSelection = null)
    {
        if (!ClassificationRowViewModel.EditableProperties.Contains(propertyName))
            return 0;

        var targets = new HashSet<ClassificationRowViewModel>(Rows.Where(r => r.IsSelected));
        if (gridSelection != null)
            foreach (var row in gridSelection)
                targets.Add(row);
        if (!targets.Contains(source))
            return 0;
        targets.Remove(source);
        if (targets.Count == 0)
            return 0;

        var changed = 0;
        foreach (var row in targets)
            if (row.CopyFrom(source, propertyName))
                changed++;

        if (changed > 0)
            StatusMessage = $"{PropertyLabel(propertyName)} de '{source.Original}' aplicado a {changed} fila(s) más de la selección.";
        return changed;
    }

    private static string PropertyLabel(string propertyName) => propertyName switch
    {
        nameof(ClassificationRowViewModel.DestinationCode) => "Código destino",
        nameof(ClassificationRowViewModel.KindLabel) => "Tipo",
        nameof(ClassificationRowViewModel.SplitBySide) => "Por lado",
        nameof(ClassificationRowViewModel.LayerBase) => "Capa",
        nameof(ClassificationRowViewModel.ColorIndex) => "Color",
        nameof(ClassificationRowViewModel.Draw) => "Dibujar",
        _ => propertyName
    };

    /// <summary>Botón "Restablecer": vuelve a la propuesta inicial (catálogo), descartando todos los cambios.</summary>
    public void ResetToInitial()
    {
        var changed = 0;
        foreach (var (row, initial) in Rows.Zip(_initialRules, (r, i) => (r, i)))
        {
            var rule = row.Rule;
            if (rule.DestinationCode == initial.DestinationCode && rule.Kind == initial.Kind && rule.SplitBySide == initial.SplitBySide &&
                rule.LayerBase == initial.LayerBase && rule.ColorIndex == initial.ColorIndex && rule.Draw == initial.Draw)
                continue;
            rule.DestinationCode = initial.DestinationCode;
            rule.Kind = initial.Kind;
            rule.SplitBySide = initial.SplitBySide;
            rule.LayerBase = initial.LayerBase;
            rule.ColorIndex = initial.ColorIndex;
            rule.Draw = initial.Draw;
            row.RaiseAll();
            changed++;
        }
        StatusMessage = changed == 0 ? "La tabla ya estaba en la propuesta inicial." : $"Se restablecieron {changed} fila(s) a la propuesta inicial.";
    }

    /// <summary>Guarda la clasificación actual como perfil. Devuelve la ruta o null si falló.</summary>
    public string? SaveProfile(string name)
    {
        if (_profiles == null)
        {
            StatusMessage = "Los perfiles no están disponibles en esta sesión.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Indique un nombre para el perfil.";
            return null;
        }
        try
        {
            var path = _profiles.Save(Profile.FromRules(name.Trim(), ToRules()));
            RefreshProfileNames();
            StatusMessage = $"Perfil guardado en {path}.";
            return path;
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo guardar el perfil: {ex.Message}";
            return null;
        }
    }

    /// <summary>Carga un perfil (por nombre o ruta) y lo aplica a las filas. Devuelve cuántas cambiaron, o -1 si falló.</summary>
    public int LoadProfile(string nameOrPath)
    {
        if (_profiles == null)
        {
            StatusMessage = "Los perfiles no están disponibles en esta sesión.";
            return -1;
        }
        try
        {
            var profile = _profiles.Load(nameOrPath);
            var changed = profile.ApplyTo(Rows.Select(r => r.Rule));
            foreach (var row in Rows)
                row.RaiseAll();
            RefreshAvailableCodes();
            RefreshVisibleRows();
            RefreshTotals();
            var unmatched = Rows.Count(r => !profile.Entries.Any(e =>
                e.Original == r.Rule.Original || DescriptionNormalizer.Normalize(e.Original) == DescriptionNormalizer.Normalize(r.Rule.Original)));
            StatusMessage = $"Perfil '{profile.Name}' aplicado: {changed} fila(s) cambiadas, {unmatched} descripción(es) sin entrada en el perfil.";
            return changed;
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo cargar el perfil: {ex.Message}";
            return -1;
        }
    }

    /// <summary>Botón "Agregar al catálogo": los códigos y alias resueltos pasan a catalogo.json.</summary>
    public bool AddToCatalog()
    {
        if (_config == null)
        {
            StatusMessage = "El catálogo no está disponible en esta sesión.";
            return false;
        }
        var merged = CatalogSerializer.Merge(_catalog, ToRules(), out var codes, out var aliases);
        if (codes == 0 && aliases == 0)
        {
            StatusMessage = "El catálogo ya contiene todos los códigos y alias de la tabla.";
            return false;
        }
        try
        {
            _config.SaveCatalog(merged);
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo guardar el catálogo: {ex.Message}";
            return false;
        }
        _catalog = merged;
        foreach (var row in Rows)
            row.UpdateCatalog(merged);
        RefreshAvailableCodes();
        StatusMessage = $"Catálogo actualizado: {codes} código(s) y {aliases} alias nuevos en {_config.Paths.CatalogFile}.";
        return true;
    }

    /// <summary>Reglas finales para el motor.</summary>
    public List<CodeRule> ToRules() => Rows.Select(r => r.Rule).ToList();

    public void RefreshProfileNames()
    {
        ProfileNames.Clear();
        if (_profiles == null)
            return;
        try
        {
            foreach (var name in _profiles.ListNames())
                ProfileNames.Add(name);
        }
        catch (Exception)
        {
            // Carpeta inaccesible: la lista queda vacía; guardar informará el error.
        }
    }

    /// <summary>true si la fila pasa el filtro actual.</summary>
    public bool Matches(ClassificationRowViewModel row)
    {
        if (ShowOnlyUnresolved && (row.IsResolved || row.Rule.Kind == CodeKind.Ignore))
            return false;
        var text = FilterText.Trim();
        if (text.Length == 0)
            return true;
        var norm = DescriptionNormalizer.Normalize(text);
        return DescriptionNormalizer.Normalize(row.Rule.Original).Contains(norm, StringComparison.Ordinal)
               || row.DestinationCode.Contains(norm, StringComparison.Ordinal)
               || DescriptionNormalizer.Normalize(row.LayerBase).Contains(norm, StringComparison.Ordinal)
               || DescriptionNormalizer.Normalize(row.Status).Contains(norm, StringComparison.Ordinal);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClassificationRowViewModel.IsSelected))
            return;
        if (e.PropertyName == nameof(ClassificationRowViewModel.DestinationCode))
            RefreshAvailableCodes();
        RefreshTotals();
        // Con un filtro activo, una fila que deja de cumplirlo (p. ej. se resolvió con "Solo sin resolver") sale de la vista.
        if (ShowOnlyUnresolved || FilterText.Trim().Length > 0)
            RefreshVisibleRows();
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

    private void RefreshVisibleRows()
    {
        var visible = Rows.Where(Matches).ToList();
        if (visible.SequenceEqual(VisibleRows))
            return;
        VisibleRows.Clear();
        foreach (var row in visible)
            VisibleRows.Add(row);
        OnPropertyChanged(nameof(VisibleCountText));
    }

    public string VisibleCountText => VisibleRows.Count == Rows.Count ? $"{Rows.Count} descripciones" : $"{VisibleRows.Count} de {Rows.Count} descripciones";

    private void RefreshTotals()
    {
        int lines = 0, pointObjects = 0, ignored = 0, unresolved = 0, total = 0, axisPoints = 0;
        var axisCode = DescriptionNormalizer.Normalize(_settings.AxisCode);
        foreach (var row in Rows)
        {
            var rule = row.Rule;
            total += rule.Count;
            if (rule.DestinationCode == axisCode)
                axisPoints += rule.Count;
            if (rule.Kind == CodeKind.Ignore)
                ignored += rule.Count;
            else if (!rule.IsResolved)
                unresolved += rule.Count;
            else if (!rule.Draw)
                ignored += rule.Count; // Casilla "Dibujar" desmarcada: el motor los trata como ignorados.
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
        AxisPointCount = axisPoints;
        OnPropertyChanged(nameof(PointsToLines));
        OnPropertyChanged(nameof(PointsToPointObjects));
        OnPropertyChanged(nameof(PointsIgnored));
        OnPropertyChanged(nameof(PointsUnresolved));
        OnPropertyChanged(nameof(TotalPoints));
        OnPropertyChanged(nameof(AxisPointCount));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CanDraw));
        OnPropertyChanged(nameof(CannotDrawReason));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
