using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
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
}