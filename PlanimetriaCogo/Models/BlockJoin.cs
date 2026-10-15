namespace PlanimetriaCogo.Models;

/// <summary>
/// Cómo se unen los puntos de un código en el modo por bloques (comando PLANIMETRIACSV), donde el orden
/// es el del archivo de campo y un "bloque" es una corrida de filas consecutivas con el mismo código destino.
/// </summary>
public enum BlockJoin
{
    /// <summary>Una polilínea abierta por cada bloque (valor por defecto).</summary>
    Block,

    /// <summary>Una polilínea cerrada (polígono) por cada bloque de 3 o más puntos: casas, losas, muros.</summary>
    ClosedBlock,

    /// <summary>Una sola polilínea con todos los puntos del código en orden de archivo, sin importar los bloques (p. ej. EJE).</summary>
    Continuous
}

/// <summary>Textos en español para la interfaz.</summary>
public static class BlockJoinNames
{
    public const string Block = "Por bloque";
    public const string ClosedBlock = "Bloque cerrado";
    public const string Continuous = "Continua";

    public static readonly string[] All = { Block, ClosedBlock, Continuous };

    public static string ToSpanish(this BlockJoin join) => join switch
    {
        BlockJoin.ClosedBlock => ClosedBlock,
        BlockJoin.Continuous => Continuous,
        _ => Block
    };

    public static BlockJoin FromSpanish(string? text) => text switch
    {
        ClosedBlock => BlockJoin.ClosedBlock,
        Continuous => BlockJoin.Continuous,
        _ => BlockJoin.Block
    };
}
