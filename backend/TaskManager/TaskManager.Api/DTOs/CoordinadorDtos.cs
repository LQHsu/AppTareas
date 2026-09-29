namespace TaskManager.Api.DTOs;

public record AltaCoordinadorDto(
    string NumeroEconomico,
    int AreaId,
    string AreaNombre,
    string CreatedById,
    string CreatedByNombre,
    DateTime CreatedAt,
    DateTime? ActivatedAt,
    // Foto de correo/area institucional tomada al dar la alta (ver
    // comentario en AltaCoordinador.cs) - null si info_usuarios_unidad
    // no tenia dato en ese momento.
    string? CorreoInstitucional,
    string? AreaInstitucional,
    // Foto del nombre completo (CUSXACDI) tomada al dar la alta - null
    // si CUSXACDI no tenia dato en ese momento.
    string? NombreCompleto,
    // Oficina (carpeta) del coordinador ya asignada a esta persona. Null
    // si no se elegio ninguna, o si se elegio y luego se borro esa
    // oficina (ver AltaCoordinador.OficinaId).
    Guid? OficinaId,
    string? OficinaNombre
);

public record CreateAltaCoordinadorDto(string NumeroEconomico, Guid? OficinaId = null);

public record UpdateAltaOficinaDto(Guid? OficinaId);

public record UpdateCoordinadorDto(bool IsCoordinador);

// Preview de datos institucionales (CUSXACDI + info_usuarios_unidad)
// para un numero economico, mostrado en la vista de "Dar de alta" antes
// de confirmar - Encontrado=false cuando ninguna de las 2 fuentes
// respondio nada (servicio caido, matricula sin registro, etc.), nunca
// bloquea seguir dando la alta a mano.
public record AltaPreviewDto(
    string NumeroEconomico,
    string? NombreCompleto,
    string? Correo,
    string? AreaInstitucional,
    bool Encontrado
);
