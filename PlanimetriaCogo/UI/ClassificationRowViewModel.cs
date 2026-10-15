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
    private string _blockInfo = "";

    public ClassificationRowViewModel(CodeRule rule, CodeCatalog catalog, Settings settings)
    {
        Rule = rule;
        _catalog = catalog;
        _settings = settings;
    }

    public CodeRule Rule { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>true en la ventana del modo por bloques: al cambiar el código destino se propone también la unión.</summary>
    public bool BlockMode { get; set; }

    /// <summary>Columna "Bloques" del modo por bloques: cuántos bloques forma la descripción y el más largo.</summary>
    public string BlockInfo
    {
        get => _blockInfo;
        set { if (_blockInfo != value) { _blockInfo = value ?? ""; OnPropertyChanged(); } }
    }

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
            ClassificationProposer.ApplyDestination(Rule, normalized, _catalog, _settings, BlockMode);
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

    /// <summary>Unión en el modo por bloques, en español: Por bloque / Bloque cerrado / Continua.</summary>
    public string BlockJoinLabel
    {
        get => Rule.BlockJoin.ToSpanish();
        set
        {
            var join = BlockJoinNames.FromSpanish(value);
            if (join == Rule.BlockJoin)
                return;
            Rule.BlockJoin = join;
            OnPropertyChanged();
        }
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

    /// <summary>Casilla "Dibujar": false deja la fila fuera del dibujo sin perder su configuración.</summary>
    public bool Draw
    {
        get => Rule.Draw;
        set
        {
            if (Rule.Draw == value)
                return;
            Rule.Draw = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
        }
    }

    public bool IsResolved => Rule.IsResolved;

    public bool IsLine => Rule.Kind == CodeKind.Line;

    /// <summary>Texto corto de estado para la columna "Estado" y los filtros.</summary>
    public string Status => Rule.Kind == CodeKind.Ignore ? "ignorar" : !Rule.IsResolved ? "sin resolver" : !Rule.Draw ? "no dibujar" : Rule.Kind == CodeKind.Point ? "punto" : "línea";

    /// <summary>Propiedades que el usuario edita en la tabla y que la edición en bloque copia a las demás filas marcadas.</summary>
    public static readonly string[] EditableProperties =
    {
        nameof(DestinationCode), nameof(KindLabel), nameof(SplitBySide), nameof(LayerBase), nameof(ColorIndex), nameof(Draw), nameof(BlockJoinLabel)
    };

    /// <summary>Valor actual de una propiedad editable (para detectar si una edición cambió algo). null si no es editable.</summary>
    public object? GetEditableValue(string propertyName) => propertyName switch
    {
        nameof(DestinationCode) => DestinationCode,
        nameof(KindLabel) => KindLabel,
        nameof(SplitBySide) => SplitBySide,
        nameof(LayerBase) => LayerBase,
        nameof(ColorIndex) => ColorIndex,
        nameof(Draw) => Draw,
        nameof(BlockJoinLabel) => BlockJoinLabel,
        _ => null
    };

    /// <summary>
    /// Copia una propiedad editable desde otra fila (edición en bloque). Pasa por el setter, así el código
    /// destino vuelve a proponer tipo, capa y color igual que si se escribiera a mano.
    /// Devuelve true si el valor de esta fila cambió.
    /// </summary>
    public bool CopyFrom(ClassificationRowViewModel source, string propertyName)
    {
        if (ReferenceEquals(source, this))
            return false;
        switch (propertyName)
        {
            case nameof(DestinationCode):
                if (DestinationCode == source.DestinationCode) return false;
                DestinationCode = source.DestinationCode;
                return true;
            case nameof(KindLabel):
                if (KindLabel == source.KindLabel) return false;
                KindLabel = source.KindLabel;
                return true;
            case nameof(SplitBySide):
                if (SplitBySide == source.SplitBySide) return false;
                SplitBySide = source.SplitBySide;
                return true;
            case nameof(LayerBase):
                if (LayerBase == source.LayerBase) return false;
                LayerBase = source.LayerBase;
                return true;
            case nameof(ColorIndex):
                if (ColorIndex == source.ColorIndex) return false;
                ColorIndex = source.ColorIndex;
                return true;
            case nameof(Draw):
                if (Draw == source.Draw) return false;
                Draw = source.Draw;
                return true;
            case nameof(BlockJoinLabel):
                if (BlockJoinLabel == source.BlockJoinLabel) return false;
                BlockJoinLabel = source.BlockJoinLabel;
                return true;
            default:
                return false;
        }
    }

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
        OnPropertyChanged(nameof(Draw));
        OnPropertyChanged(nameof(BlockJoinLabel));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(IsLine));
        OnPropertyChanged(nameof(Status));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
