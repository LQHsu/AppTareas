using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Api.Institucional;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

// Panel del coordinador: da de alta numeros economicos autorizados a
// entrar a la app (ver AltaCoordinador y el gate en
// UsersController.Create). Especifico del proceso institucional de
// UAMX - no se sube al repo general, ver README de esta carpeta si
// existe una nota al respecto.
//
// Siempre scopeado al area del que llama (coordinador o super admin
// por igual): un coordinador solo puede dar de alta gente para SU
// propia area, nunca elige otra. Un super admin que necesite dar altas
// en varias areas necesita el flag IsCoordinador ademas, y entra una
// vez por cada area (cambiando su propia AreaId no es el flujo - en la
// practica cada super admin ya vive en un area fija).
[ApiController]
[Route("api/coordinador")]
[Authorize]
public class CoordinadorController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CusxacdiInfoClient _cusxacdi;
    private readonly InfoUsuariosUnidadClient _infoUsuariosUnidad;

    public CoordinadorController(AppDbContext db, CusxacdiInfoClient cusxacdi, InfoUsuariosUnidadClient infoUsuariosUnidad)
    {
        _db = db;
        _cusxacdi = cusxacdi;
        _infoUsuariosUnidad = infoUsuariosUnidad;
    }

    // GET /api/coordinador/altas/preview/{numeroEconomico}
    // Solo lectura (no crea nada) - trae nombre completo (CUSXACDI) y
    // correo/area institucional (info_usuarios_unidad) para que el
    // coordinador confirme que esta dando de alta a la persona correcta
    // antes de mandar el POST real. El area institucional es solo
    // informativa: el area que de verdad se asigna sigue siendo la del
    // coordinador (ver CreateAlta abajo), nunca la que venga de aca.
    [HttpGet("altas/preview/{numeroEconomico}")]
    public async Task<ActionResult<AltaPreviewDto>> PreviewAlta(string numeroEconomico)
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        numeroEconomico = numeroEconomico.Trim();
        if (string.IsNullOrWhiteSpace(numeroEconomico))
            return BadRequest("El numero economico no puede estar vacio.");

        var cuentaTask = _cusxacdi.RecuperaInfoAsync(numeroEconomico);
        var datosTask = _infoUsuariosUnidad.BuscarAsync(numeroEconomico);
        await Task.WhenAll(cuentaTask, datosTask);

        var cuenta = await cuentaTask;
        var datos = await datosTask;

        if (cuenta is null && datos is null)
            return Ok(new AltaPreviewDto(numeroEconomico, null, null, null, false));

        return Ok(new AltaPreviewDto(
            numeroEconomico,
            cuenta?.NombreCompleto,
            datos?.Email,
            datos?.Area,
            true));
    }

    // GET /api/coordinador/altas
    [HttpGet("altas")]
    public async Task<ActionResult<IEnumerable<AltaCoordinadorDto>>> GetAltas()
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        var altas = await _db.AltasCoordinador
            .Include(a => a.Area)
            .Include(a => a.CreatedBy)
            .Include(a => a.Oficina)
            .Where(a => a.AreaId == caller.AreaId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var dtos = new List<AltaCoordinadorDto>(altas.Count);
        foreach (var alta in altas)
            dtos.Add(await ToDtoAsync(alta, caller.Id));

        return Ok(dtos);
    }

    // POST /api/coordinador/altas
    // El numero economico se normaliza (trim) - se compara tal cual
    // contra lo que UsersController.Create extrae del token, sin mas
    // reglas de formato: el numero economico institucional no es
    // siempre puramente numerico (hay excepciones historicas), asi que
    // no se valida un patron rigido aca.
    [HttpPost("altas")]
    public async Task<ActionResult<AltaCoordinadorDto>> CreateAlta(CreateAltaCoordinadorDto dto)
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        var numeroEconomico = dto.NumeroEconomico?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(numeroEconomico))
            return BadRequest("El numero economico no puede estar vacio.");

        var yaExiste = await _db.AltasCoordinador.AnyAsync(a => a.NumeroEconomico == numeroEconomico);
        if (yaExiste) return Conflict("Ese numero economico ya esta dado de alta.");

        // La oficina (carpeta) elegida tiene que ser propia del
        // coordinador: es su propia carpeta la que se comparte con la
        // persona en cuanto active su cuenta (ver UsersController.Create).
        ProjectFolder? oficina = null;
        if (dto.OficinaId.HasValue)
        {
            oficina = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == dto.OficinaId.Value);
            if (oficina is null) return BadRequest("La oficina elegida no existe.");
            if (oficina.OwnerId != caller.Id) return BadRequest("Solo puedes asignar oficinas propias.");
        }

        // Misma consulta que PreviewAlta: se guarda como foto del momento,
        // no se vuelve a repetir despues (ver comentario en la entidad).
        var cuentaTask = _cusxacdi.RecuperaInfoAsync(numeroEconomico);
        var datosTask = _infoUsuariosUnidad.BuscarAsync(numeroEconomico);
        await Task.WhenAll(cuentaTask, datosTask);

        var cuenta = await cuentaTask;
        var datos = await datosTask;

        var alta = new AltaCoordinador
        {
            NumeroEconomico = numeroEconomico,
            AreaId = caller.AreaId,
            CreatedById = caller.Id,
            CreatedAt = DateTime.UtcNow,
            ActivatedAt = null,
            CorreoInstitucional = datos?.Email,
            AreaInstitucional = datos?.Area,
            NombreCompleto = cuenta?.NombreCompleto,
            OficinaId = oficina?.Id,
        };

        _db.AltasCoordinador.Add(alta);
        await _db.SaveChangesAsync();

        alta.Area = caller.Area;
        alta.CreatedBy = caller;
        alta.Oficina = oficina;

        return Ok(await ToDtoAsync(alta, caller.Id));
    }

    // PATCH /api/coordinador/altas/{numeroEconomico}/oficina
    // Cambiar la oficina asignada directo desde la tabla. Solo mientras
    // la alta sigue pendiente: una vez activada, la oficina original ya
    // se comparcio de verdad (ver UsersController.Create) y cambiarla
    // aqui NO revertiria ni reasignaria nada - para mover a esa persona
    // de oficina despues de activada hay que usar Compartir en
    // /carpetas, que si hace la reasignacion de tareas correspondiente.
    [HttpPatch("altas/{numeroEconomico}/oficina")]
    public async Task<ActionResult<AltaCoordinadorDto>> UpdateOficina(string numeroEconomico, UpdateAltaOficinaDto dto)
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        var alta = await _db.AltasCoordinador
            .Include(a => a.Area)
            .Include(a => a.CreatedBy)
            .FirstOrDefaultAsync(a => a.NumeroEconomico == numeroEconomico && a.AreaId == caller.AreaId);
        if (alta is null) return NotFound();

        if (alta.ActivatedAt is not null)
            return BadRequest("Esa persona ya activo su cuenta, no se puede cambiar su oficina desde aqui.");

        ProjectFolder? oficina = null;
        if (dto.OficinaId.HasValue)
        {
            oficina = await _db.ProjectFolders.FirstOrDefaultAsync(f => f.Id == dto.OficinaId.Value);
            if (oficina is null) return BadRequest("La oficina elegida no existe.");
            if (oficina.OwnerId != caller.Id) return BadRequest("Solo puedes asignar oficinas propias.");
        }

        alta.OficinaId = oficina?.Id;
        alta.Oficina = oficina;
        await _db.SaveChangesAsync();

        return Ok(await ToDtoAsync(alta, caller.Id));
    }

    // POST /api/coordinador/altas/{numeroEconomico}/refrescar
    // Vuelve a consultar CUSXACDI/info_usuarios_unidad y actualiza el
    // snapshot (nombre completo, correo y area institucional) - a
    // diferencia del resto de esta pantalla, esto SI funciona aunque la
    // persona ya haya activado su cuenta: esta pantalla tambien sirve
    // para monitorear el estado institucional de gente que ya entro a
    // la app, no solo para dar altas nuevas.
    [HttpPost("altas/{numeroEconomico}/refrescar")]
    public async Task<ActionResult<AltaCoordinadorDto>> RefrescarDatos(string numeroEconomico)
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        var alta = await _db.AltasCoordinador
            .Include(a => a.Area)
            .Include(a => a.CreatedBy)
            .Include(a => a.Oficina)
            .FirstOrDefaultAsync(a => a.NumeroEconomico == numeroEconomico && a.AreaId == caller.AreaId);
        if (alta is null) return NotFound();

        var cuentaTask = _cusxacdi.RecuperaInfoAsync(numeroEconomico);
        var datosTask = _infoUsuariosUnidad.BuscarAsync(numeroEconomico);
        await Task.WhenAll(cuentaTask, datosTask);

        var cuenta = await cuentaTask;
        var datos = await datosTask;

        alta.NombreCompleto = cuenta?.NombreCompleto;
        alta.CorreoInstitucional = datos?.Email;
        alta.AreaInstitucional = datos?.Area;

        await _db.SaveChangesAsync();

        return Ok(await ToDtoAsync(alta, caller.Id));
    }

    // DELETE /api/coordinador/altas/{numeroEconomico}
    // Solo si sigue pendiente: una vez que alguien ya activo su cuenta
    // con ese numero, el renglon es bitacora de un User real que ya
    // existe - borrarlo de aca no borraria a esa persona, solo
    // confundiria el historial, asi que no se permite.
    [HttpDelete("altas/{numeroEconomico}")]
    public async Task<IActionResult> DeleteAlta(string numeroEconomico)
    {
        var caller = await GetCallerOrForbid();
        if (caller is null) return Forbid();

        var alta = await _db.AltasCoordinador
            .FirstOrDefaultAsync(a => a.NumeroEconomico == numeroEconomico && a.AreaId == caller.AreaId);
        if (alta is null) return NotFound();

        if (alta.ActivatedAt is not null)
            return BadRequest("Esa persona ya activo su cuenta, no se puede quitar de aqui.");

        _db.AltasCoordinador.Remove(alta);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Arma el DTO resolviendo la oficina VIGENTE, no la congelada en
    // AltaCoordinador.OficinaId (que solo describe la oficina de ANTES
    // de activarse - ver comentario en la entidad). Mientras sigue
    // pendiente no hay nada que resolver en vivo todavia, asi que se
    // usa el campo tal cual.
    private async Task<AltaCoordinadorDto> ToDtoAsync(AltaCoordinador alta, string callerId)
    {
        Guid? oficinaId = alta.OficinaId;
        string? oficinaNombre = alta.Oficina?.Name;

        if (alta.ActivatedAt is not null && alta.UserId is not null)
        {
            // La carpeta (propia del coordinador) compartida con esa
            // persona AHORA MISMO - no depende de por donde se le haya
            // compartido (al activarse, o despues a mano desde
            // /carpetas). Si se comparte mas de una con la misma
            // persona, se toma la mas reciente.
            var vigente = await _db.ProjectFolders
                .Where(f => f.OwnerId == callerId && f.SharedWithId == alta.UserId)
                .OrderByDescending(f => f.SharedAt)
                .Select(f => new { f.Id, f.Name })
                .FirstOrDefaultAsync();

            oficinaId = vigente?.Id;
            oficinaNombre = vigente?.Name;
        }

        return new AltaCoordinadorDto(
            alta.NumeroEconomico, alta.AreaId, alta.Area.Nombre,
            alta.CreatedById, alta.CreatedBy.FullName, alta.CreatedAt, alta.ActivatedAt,
            alta.CorreoInstitucional, alta.AreaInstitucional, alta.NombreCompleto,
            oficinaId, oficinaNombre);
    }

    // Regresa null (y ya dejo la respuesta en Forbid via el caller) si
    // quien llama no es coordinador ni super admin.
    private async Task<User?> GetCallerOrForbid()
    {
        var callerId = GetUserIdFromToken();
        var caller = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == callerId);
        if (caller is null || !(caller.IsCoordinador || caller.IsSuperAdmin)) return null;
        return caller;
    }

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        return sub.ToLowerInvariant();
    }
}
