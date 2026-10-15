using System.Globalization;
using System.Text;

namespace PlanimetriaCogo.Core;

/// <summary>
/// Normaliza descripciones de campo: mayúsculas, sin tildes, sin espacios sobrantes.
/// "  cumeta " → "CUMETA"; "Árbol  grande" → "ARBOL GRANDE". La Ñ se conserva.
/// </summary>
public static class DescriptionNormalizer
{
    public static string Normalize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";

        // Se protege la Ñ para que la eliminación de diacríticos no la convierta en N.
        var text = description.Replace('ñ', '\u0001').Replace('Ñ', '\u0001');

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // evita espacios al inicio y dobles

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            sb.Append(ch == '\u0001' ? 'Ñ' : char.ToUpperInvariant(ch));
            lastWasSpace = false;
        }

        return sb.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    /// <summary>true si la descripción normalizada está vacía o es solo dígitos (puntos de control).</summary>
    public static bool IsEmptyOrNumeric(string normalized)
    {
        if (normalized.Length == 0)
            return true;
        foreach (var ch in normalized)
        {
            if (!char.IsDigit(ch) && ch != '.' && ch != ' ')
                return false;
        }
        return true;
    }
}
