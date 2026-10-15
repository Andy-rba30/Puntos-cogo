using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using PlanimetriaCogo.Config;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.UI;

/// <summary>
/// Copia de trabajo de <see cref="Settings"/> para la ventana de parámetros (PLANIMETRIACONFIG y botón
/// "Parámetros…"). Los cambios solo pasan al objeto real con <see cref="Apply"/>. Sin dependencias de WPF.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Settings _target;
    private readonly Settings _work;

    public SettingsViewModel(Settings target, ConfigPaths? paths = null)
    {
        _target = target;
        _work = target.Clone();
        ConfigFolder = paths?.Root ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Carpeta de configuración, para mostrarla y abrirla desde la ventana.</summary>
    public string ConfigFolder { get; }

    public IReadOnlyList<string> OrderOptions { get; } = new[] { "Número de punto", "Progresiva" };

    public IReadOnlyList<string> CsvColumnOrderOptions { get; } =
        new[] { "PNEZD (número, norte, este, cota, descripción)", "PENZD (número, este, norte, cota, descripción)" };

    // ---- Eje y lado ----
    public double OffsetMax { get => _work.OffsetMax; set => Set(v => _work.OffsetMax = v, value); }
    public double AxisTolerance { get => _work.AxisTolerance; set => Set(v => _work.AxisTolerance = v, value); }
    public double AxisSampleStep { get => _work.AxisSampleStep; set => Set(v => _work.AxisSampleStep = v, value); }
    public string AxisCode { get => _work.AxisCode; set => Set(v => _work.AxisCode = v, value); }
    public string GeneratedAxisLayer { get => _work.GeneratedAxisLayer; set => Set(v => _work.GeneratedAxisLayer = v, value); }
    public bool KeepGeneratedAxis { get => _work.KeepGeneratedAxis; set => Set(v => _work.KeepGeneratedAxis = v, value); }

    // ---- Orden y cortes ----
    public string OrderLabel
    {
        get => _work.Order == PointOrder.Station ? OrderOptions[1] : OrderOptions[0];
        set => Set(v => _work.Order = v == OrderOptions[1] ? PointOrder.Station : PointOrder.Number, value);
    }
    public double DistMax { get => _work.DistMax; set => Set(v => _work.DistMax = v, value); }
    public double StationGapMax { get => _work.StationGapMax; set => Set(v => _work.StationGapMax = v, value); }
    public bool UseDescriptionMarks { get => _work.UseDescriptionMarks; set => Set(v => _work.UseDescriptionMarks = v, value); }
    public bool SplitSeries { get => _work.SplitSeries; set => Set(v => _work.SplitSeries = v, value); }

    /// <summary>Las listas de marcas se editan como texto separado por comas.</summary>
    public string StartMarksText { get => Join(_work.StartMarks); set => Set(v => _work.StartMarks = SplitMarks(v), value); }
    public string EndMarksText { get => Join(_work.EndMarks); set => Set(v => _work.EndMarks = SplitMarks(v), value); }
    public string LeftMarksText { get => Join(_work.LeftMarks); set => Set(v => _work.LeftMarks = SplitMarks(v), value); }
    public string RightMarksText { get => Join(_work.RightMarks); set => Set(v => _work.RightMarks = SplitMarks(v), value); }
    public string BlockStartMarksText { get => Join(_work.BlockStartMarks); set => Set(v => _work.BlockStartMarks = SplitMarks(v), value); }

    // ---- Modo por bloques (PLANIMETRIACSV) ----

    /// <summary>Códigos con bloque cerrado, como texto separado por comas.</summary>
    public string ClosedBlockCodesText { get => Join(_work.ClosedBlockCodes); set => Set(v => _work.ClosedBlockCodes = SplitMarks(v), value); }

    public string CsvColumnOrderLabel
    {
        get => _work.CsvColumnOrder == CsvColumnOrder.PENZD ? CsvColumnOrderOptions[1] : CsvColumnOrderOptions[0];
        set => Set(v => _work.CsvColumnOrder = v == CsvColumnOrderOptions[1] ? CsvColumnOrder.PENZD : CsvColumnOrder.PNEZD, value);
    }

    // ---- Acciones sobre el dibujo ----
    public bool MovePointObjectsToLayer { get => _work.MovePointObjectsToLayer; set => Set(v => _work.MovePointObjectsToLayer = v, value); }
    public bool UpdateDescriptionsInDrawing { get => _work.UpdateDescriptionsInDrawing; set => Set(v => _work.UpdateDescriptionsInDrawing = v, value); }

    // ---- Reporte ----
    public bool ReportToFile { get => _work.ReportToFile; set => Set(v => _work.ReportToFile = v, value); }
    public string ReportFolder { get => _work.ReportFolder; set => Set(v => _work.ReportFolder = v, value); }

    // ---- Clasificación y capas ----
    public bool UseDescriptionAsCode { get => _work.UseDescriptionAsCode; set => Set(v => _work.UseDescriptionAsCode = v, value); }
    public bool SingleLayer { get => _work.SingleLayer; set => Set(v => _work.SingleLayer = v, value); }
    public string SingleLayerName { get => _work.SingleLayerName; set => Set(v => _work.SingleLayerName = v, value); }
    public short SingleLayerColor { get => _work.SingleLayerColor; set => Set(v => _work.SingleLayerColor = v, value); }
    public string LineLayerPrefix { get => _work.LineLayerPrefix; set => Set(v => _work.LineLayerPrefix = v, value); }
    public string PointLayerPrefix { get => _work.PointLayerPrefix; set => Set(v => _work.PointLayerPrefix = v, value); }
    public string LeftLayerSuffix { get => _work.LeftLayerSuffix; set => Set(v => _work.LeftLayerSuffix = v, value); }
    public string RightLayerSuffix { get => _work.RightLayerSuffix; set => Set(v => _work.RightLayerSuffix = v, value); }

    /// <summary>Errores de validación; vacío si todo es válido.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (_work.OffsetMax < 0 || double.IsNaN(_work.OffsetMax)) errors.Add("offsetMax no puede ser negativo (0 = sin límite).");
        if (_work.DistMax < 0 || double.IsNaN(_work.DistMax)) errors.Add("distMax no puede ser negativo (0 = sin límite).");
        if (_work.StationGapMax < 0 || double.IsNaN(_work.StationGapMax)) errors.Add("El salto de progresiva no puede ser negativo (0 = sin límite).");
        if (_work.AxisTolerance < 0 || double.IsNaN(_work.AxisTolerance)) errors.Add("tolEje no puede ser negativa.");
        if (_work.AxisSampleStep <= 0 || double.IsNaN(_work.AxisSampleStep)) errors.Add("El paso de muestreo debe ser mayor que 0.");
        if (_work.OffsetMax > 0 && _work.AxisTolerance >= _work.OffsetMax) errors.Add("tolEje debe ser menor que offsetMax.");
        if (string.IsNullOrWhiteSpace(_work.AxisCode)) errors.Add("Indique el código del eje (por ejemplo EJE).");
        if (string.IsNullOrWhiteSpace(_work.GeneratedAxisLayer)) errors.Add("Indique la capa del eje generado.");
        if (string.IsNullOrWhiteSpace(_work.LineLayerPrefix) && string.IsNullOrWhiteSpace(_work.PointLayerPrefix))
            errors.Add("Indique al menos un prefijo de capa.");
        if (_work.SingleLayer && string.IsNullOrWhiteSpace(_work.SingleLayerName))
            errors.Add("Indique el nombre de la capa única.");
        if (_work.SingleLayerColor is <= 0 or >= 256)
            errors.Add("El color de la capa única debe ser un índice ACI entre 1 y 255.");
        if (_work.UseDescriptionMarks)
        {
            var all = _work.StartMarks.Concat(_work.EndMarks).Concat(_work.LeftMarks).Concat(_work.RightMarks).Concat(_work.BlockStartMarks)
                .Select(Core.DescriptionNormalizer.Normalize).Where(m => m.Length > 0).ToList();
            var repeated = all.GroupBy(m => m).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repeated.Count > 0)
                errors.Add("Una marca no puede estar en dos listas: " + string.Join(", ", repeated) + ".");
        }
        return errors;
    }

    /// <summary>Copia la copia de trabajo al objeto real. Devuelve false (sin copiar) si hay errores.</summary>
    public bool Apply()
    {
        if (Validate().Count > 0)
            return false;
        ConfigService.Sanitize(_work);
        _target.CopyFrom(_work);
        return true;
    }

    public void ResetDefaults()
    {
        _work.CopyFrom(new Settings());
        RaiseAll();
    }

    private static string Join(IEnumerable<string> marks) => string.Join(", ", marks);

    private static List<string> SplitMarks(string? text) =>
        (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Set<T>(Action<T> assign, T value, [CallerMemberName] string? name = null)
    {
        assign(value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void RaiseAll()
    {
        foreach (var property in GetType().GetProperties().Where(p => p.CanRead))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property.Name));
    }

    /// <summary>Formato de número para la interfaz (punto decimal, sin ceros sobrantes).</summary>
    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
