using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/users/me
    // Se llama justo despues del login. Si el usuario ya existe en
    // nuestra tabla local, regresa sus datos; si es la primera vez
    // que entra (viene de Keycloak/LDAP pero nunca toco esta app),
    // el frontend debe mandarlo a completar su Area con POST /api/users.
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe()
    {
        var keycloakId = GetUserIdFromToken();

        var user = await _db.Users
            .Include(u => u.Area)
            .Where(u => u.Id == keycloakId)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned))
            .FirstOrDefaultAsync();

        if (user is null) return NotFound(); // el frontend interpreta esto como "falta completar registro"

        return Ok(user);
    }

    // GET /api/users?areaId=3
    // Util para invitar gente a un proyecto (solo del area del proyecto)
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll([FromQuery] int? areaId)
    {
        var query = _db.Users.Include(u => u.Area).AsQueryable();

        if (areaId.HasValue)
            query = query.Where(u => u.AreaId == areaId.Value);

        var users = await query
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned))
            .ToListAsync();

        return Ok(users);
    }

    // POST /api/users
    // Auto-registro la primera vez que un usuario valido de Keycloak entra a la app.
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserDto dto)
    {
        var exists = await _db.Users.AnyAsync(u => u.Id == dto.Id);
        if (exists) return Conflict("El usuario ya esta registrado.");

        var areaExists = await _db.Areas.AnyAsync(a => a.Id == dto.AreaId);
        if (!areaExists) return BadRequest("Area invalida.");

        var user = new User
        {
            Id = dto.Id,
            Username = dto.Username,
            Email = dto.Email,
            FullName = dto.FullName,
            AreaId = dto.AreaId,
            IsSuperAdmin = false, // el super admin se asigna manualmente, nunca por auto-registro
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var area = await _db.Areas.FindAsync(dto.AreaId);

        return CreatedAtAction(nameof(GetMe), new UserDto(user.Id, user.Username, user.Email, user.FullName, user.AreaId, area!.Nombre, user.IsSuperAdmin, user.IsBanned));
    }

    private Guid GetUserIdFromToken()
    {
        // El "sub" del JWT de Keycloak es el identificador estable del usuario
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        return Guid.Parse(sub);
    }
}