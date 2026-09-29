using System.Text.RegularExpressions;

namespace TaskManager.Api;

// Validacion compartida por FoldersController/ProjectsController: el
// color es opcional (null = "sin color", siempre valido), pero si viene
// debe ser un hex de 6 digitos ("#RRGGBB").
public static class ColorValidation
{
    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$");

    public static bool IsValidHex(string? color) => color is null || HexColorRegex.IsMatch(color);
}
