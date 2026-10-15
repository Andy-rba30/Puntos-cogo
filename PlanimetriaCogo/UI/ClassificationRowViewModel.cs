using System.ComponentModel;
using System.Runtime.CompilerServices;
using PlanimetriaCogo.Core;
using PlanimetriaCogo.Models;

namespace PlanimetriaCogo.UI;

/// <summary>Fila de la tabla de clasificación. Envuelve una <see cref="CodeRule"/> con notificación de cambios.</summary>
public sealed class ClassificationRowViewModel : INotifyPropertyChanged
{
    private CodeCatalog _catalog;
    private readonly Settings _settings;
    private bool _isSelected;

    public ClassificationRowViewModel(CodeRule rule, CodeCatalog catalog, Settings settings)
    {
        Rule = rule;
        _catalog = catalog;
        _settings = settings;
    }

    public CodeRule Rule { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Marca para "Unificar selección".</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string Original => Rule.Original.Length == 0 ? "(sin descripción)" : Rule.Original;

    public int Count => Rule.Count;

    /// <summary>Código destino. Al cambiarlo se vuelven a proponer tipo, lados, capa y color.</summary>
    public string DestinationCode
    {
        get => Rule.DestinationCode;
        set
        {
            var normalized = DescriptionNormalizer.Normalize(value);
            if (normalized == Rule.DestinationCode)
                return;
            ClassificationProposer.ApplyDestination(Rule, normalized, _catalog, _settings);
            RaiseAll();
        }
    }

    /// <summary>Tipo mostrado en español: Línea / Punto / Ignorar.</summary>
    public string KindLabel
    {
        get => Rule.Kind.ToSpanish();
        set
        {
            var kind = CodeKindNames.FromSpanish(value);
            if (kind == Rule.Kind)
                return;
            Rule.Kind = kind;
            Rule.LayerBase = LayerNames.ProposeBase(Rule, _settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LayerBase));
            OnPropertyChanged(nameof(IsLine));
        }
    }

    public bool SplitBySide
    {
        get => Rule.SplitBySide;
        set { if (Rule.SplitBySide != value) { Rule.SplitBySide = value; OnPropertyChanged(); } }
    }

    public string LayerBase
    {
        get => Rule.LayerBase;
        set
        {
            var sanitized = LayerNames.Sanitize(value);
            if (sanitized == Rule.LayerBase)
                return;
            Rule.LayerBase = sanitized;
            OnPropertyChanged();
        }
    }

    public short ColorIndex
    {
        get => Rule.ColorIndex;
        set
        {
            var color = value is > 0 and < 256 ? value : (short)7;
            if (color == Rule.ColorIndex)
                return;
            Rule.ColorIndex = color;
            OnPropertyChanged();
        }
    }

    public bool IsResolved => Rule.IsResolved;

    public bool IsLine => Rule.Kind == CodeKind.Line;

    /// <summary>Texto corto de estado para la columna "Estado" y los filtros.</summary>
    public string Status => Rule.Kind == CodeKind.Ignore ? "ignorar" : !Rule.IsResolved ? "sin resolver" : Rule.Kind == CodeKind.Point ? "punto" : "línea";

    /// <summary>Usa otro catálogo (tras "Agregar al catálogo").</summary>
    public void UpdateCatalog(CodeCatalog catalog) => _catalog = catalog;

    /// <summary>Notifica todas las propiedades (tras cargar un perfil o restablecer, que cambian la regla por fuera).</summary>
    public void RaiseAll()
    {
        OnPropertyChanged(nameof(DestinationCode));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(SplitBySide));
        OnPropertyChanged(nameof(LayerBase));
        OnPropertyChanged(nameof(ColorIndex));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(IsLine));
        OnPropertyChanged(nameof(Status));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
