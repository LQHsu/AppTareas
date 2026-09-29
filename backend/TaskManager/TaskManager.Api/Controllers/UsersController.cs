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
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned, u.IsCoordinador, u.NotifyByEmail, u.NotifyByChat))
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
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.AreaId, u.Area.Nombre, u.IsSuperAdmin, u.IsBanned, u.IsCoordinador, u.NotifyByEmail, u.NotifyByChat))
            .ToListAsync();

        return Ok(users);
    }

    // POST /api/users
    // Ya NO es auto-registro abierto: un login valido de Keycloak
    // (CUSXACDI) por si solo no basta, hace falta que un coordinador de
    // area haya dado de alta el numero economico de antemano (ver
    // AltaCoordinador/CoordinadorController). Especifico del proceso
    // institucional de UAMX.
    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserDto dto)
    {
        var id = dto.Id.ToLowerInvariant(); // ver comentario en User.Id

        var exists = await _db.Users.AnyAsync(u => u.Id == id);
        if (exists) return Conflict("El usuario ya esta registrado.");

        var numeroEconomico = ExtraerNumeroEconomico(id);
        var alta = await _db.AltasCoordinador
            .Include(a => a.Area)
            .Include(a => a.Oficina)
            .ThenInclude(o => o!.Projects)
            .FirstOrDefaultAsync(a => a.NumeroEconomico == numeroEconomico);

        // Cuerpo propio (no Forbid() plano) para que el frontend pueda
        // distinguir este caso de un 403 generico y mostrar un mensaje
        // util - mismo patron que BanCheckMiddleware con "account_banned".
        if (alta is null)
            return StatusCode(403, new
            {
                error = "not_whitelisted",
                message = "Tu número económico no está dado de alta en el sistema. Contacta a tu coordinador de área.",
            });

        // AreaId viene del alta, no de dto.AreaId: quien lo dio de alta
        // decide el area, la persona no se autoasigna a otra al
        // completar su registro (dto.AreaId se ignora a proposito).
        var user = new User
        {
            Id = id,
            Username = dto.Username,
            Email = dto.Email,
            FullName = dto.FullName,
            AreaId = alta.AreaId,
            IsSuperAdmin = false, // el super admin se asigna manualmente, nunca por auto-registro
            IsCoordinador = false,
            IsBanned = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        alta.ActivatedAt = DateTime.UtcNow;
        alta.UserId = user.Id;

        // Si el coordinador ya le habia elegido una oficina al dar la
        // alta, se comparte con el aqui mismo - mismo efecto que
        // FoldersController.Share (se agrega a sus proyectos y se le
        // reasignan todas sus tareas), sin que la persona tenga que
        // esperar a que alguien la comparta a mano despues.
        if (alta.Oficina is not null)
            await FolderSharing.ShareAsync(_db, alta.Oficina, user, alta.CreatedById);

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMe), new UserDto(user.Id, user.Username, user.Email, user.FullName, user.AreaId, alta.Area.Nombre, user.IsSuperAdmin, user.IsBanned, user.IsCoordinador, user.NotifyByEmail, user.NotifyByChat));
    }

    // PATCH /api/users/me/notifications
    // Unica forma de tocar estos dos campos - cada quien decide sus
    // propios canales, no hay pantalla de admin para forzarlos (ver
    // comentario en User.NotifyByEmail/NotifyByChat).
    [HttpPatch("me/notifications")]
    public async Task<ActionResult<UserDto>> UpdateNotificationPreferences(UpdateNotificationPreferencesDto dto)
    {
        var keycloakId = GetUserIdFromToken();

        var user = await _db.Users.Include(u => u.Area).FirstOrDefaultAsync(u => u.Id == keycloakId);
        if (user is null) return NotFound();

        user.NotifyByEmail = dto.NotifyByEmail;
        user.NotifyByChat = dto.NotifyByChat;
        await _db.SaveChangesAsync();

        return Ok(new UserDto(user.Id, user.Username, user.Email, user.FullName, user.AreaId, user.Area.Nombre, user.IsSuperAdmin, user.IsBanned, user.IsCoordinador, user.NotifyByEmail, user.NotifyByChat));
    }

    // El "sub" de un usuario federado (ver CusxacdiUserStorageProvider en
    // docker/keycloak/) tiene forma "f:<uuid-del-provider>:<matricula>" -
    // AltaCoordinador solo guarda la matricula pelada porque es lo unico
    // que el coordinador conoce de antemano. Si el id no trae ese
    // formato (cuentas semilla/dev, ya registradas de antemano por SQL y
    // que nunca deberian llegar a este metodo) se regresa tal cual, sin
    // que rompa - simplemente no va a matchear ningun alta.
    private static string ExtraerNumeroEconomico(string id)
    {
        var partes = id.Split(':');
        return partes.Length == 3 && partes[0] == "f" ? partes[2] : id;
    }

    private string GetUserIdFromToken()
    {
        // El "sub" del JWT de Keycloak es el identificador estable del usuario
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}