using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;
 
namespace TaskManager.Api.Controllers;
 
[ApiController]
[Route("api/[controller]")]
[Authorize] // requiere JWT valido de Keycloak
public class AreasController : ControllerBase
{
    private readonly AppDbContext _db;
 
    public AreasController(AppDbContext db)
    {
        _db = db;
    }
 
    // GET /api/areas
    // Catalogo simple, usado para llenar selects en el frontend
    // (ej. al invitar gente a un proyecto, filtrar por area).
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AreaDto>>> GetAll()
    {
        var areas = await _db.Areas
            .OrderBy(a => a.Nombre)
            .Select(a => new AreaDto(a.Id, a.Nombre))
            .ToListAsync();
 
        return Ok(areas);
    }
 
    // GET /api/areas/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AreaDto>> GetById(int id)
    {
        var area = await _db.Areas.FindAsync(id);
        if (area is null) return NotFound();

        return Ok(new AreaDto(area.Id, area.Nombre));
    }

    // POST /api/areas/resolve
    // Busca un Area por nombre exacto; si no existe, la crea. Es la pieza
    // de "sincronizacion perezosa" desde la BD institucional (ver
    // comentario en Area.cs): en vez de un job que precargue el catalogo
    // completo, cada Area nueva se crea sola la primera vez que un
    // usuario de esa area entra a la app (completar-registro llama esto
    // con el nombre que trae el claim "area" del token de Keycloak, ver
    // docker/keycloak/README.md). Idempotente para llamadas concurrentes:
    // si dos usuarios de una area nueva se registran al mismo tiempo, el
    // segundo INSERT falla por el indice unico y esta se recupera
    // reconsultando en vez de propagar el error.
    [HttpPost("resolve")]
    public async Task<ActionResult<AreaDto>> Resolve(ResolveAreaDto dto)
    {
        var nombre = dto.Nombre?.Trim() ?? string.Empty;
        if (nombre.Length == 0) return BadRequest("El nombre del area no puede estar vacio.");

        var existente = await _db.Areas.FirstOrDefaultAsync(a => a.Nombre == nombre);
        if (existente is not null) return Ok(new AreaDto(existente.Id, existente.Nombre));

        var area = new Area { Nombre = nombre };
        _db.Areas.Add(area);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Carrera: otra request creo la misma area entre el
            // FirstOrDefaultAsync y este SaveChangesAsync.
            var creadaPorOtro = await _db.Areas.FirstOrDefaultAsync(a => a.Nombre == nombre);
            if (creadaPorOtro is not null) return Ok(new AreaDto(creadaPorOtro.Id, creadaPorOtro.Nombre));
            throw;
        }

        return Ok(new AreaDto(area.Id, area.Nombre));
    }
}