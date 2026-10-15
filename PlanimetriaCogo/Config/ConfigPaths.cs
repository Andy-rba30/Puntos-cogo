using System;
using System.IO;
using System.Text;

namespace PlanimetriaCogo.Config;

/// <summary>
/// Rutas de configuración del plugin: <c>%APPDATA%\PlanimetriaCogo\</c> con <c>config.json</c>,
/// <c>catalogo.json</c>, <c>perfiles\</c> y <c>reportes\</c>. La raíz se puede cambiar (pruebas).
/// </summary>
public sealed class ConfigPaths
{
    public ConfigPaths(string root)
    {
        Root = root;
    }

    public static ConfigPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlanimetriaCogo"));

    public string Root { get; }
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string CatalogFile => Path.Combine(Root, "catalogo.json");
    public string ProfilesDir => Path.Combine(Root, "perfiles");
    public string ReportsDir => Path.Combine(Root, "reportes");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(ReportsDir);
    }

    /// <summary>Convierte un nombre libre en nombre de archivo seguro ("Vía norte / 2026" → "Vía norte _ 2026").</summary>
    public static string SafeFileName(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0)
            return "perfil";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }
}
