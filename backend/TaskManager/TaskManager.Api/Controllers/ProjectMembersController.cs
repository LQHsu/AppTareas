using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.DTOs;
using TaskManager.Domain.Entities;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/members")]
[Authorize]
public class ProjectMembersController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProjectMembersController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/projects/{projectId}/members
    // Cualquier miembro del proyecto puede ver la lista completa.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectMemberDto>>> GetAll(Guid projectId)
    {
        var userId = GetUserIdFromToken();

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound();

        var isMember = await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
        if (!isMember && project.OwnerId != userId) return Forbid();

        var members = await _db.ProjectMembers
            .Include(m => m.User)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.User.FullName)
            .Select(m => new ProjectMemberDto(
                m.UserId,
                m.User.FullName,
                m.User.Email,
                m.JoinedAt,
                m.UserId == project.OwnerId
            ))
            .ToListAsync();

        return Ok(members);
    }

    // POST /api/projects/{projectId}/members
    // Solo el dueno del proyecto puede invitar gente, y solo dentro
    // de la misma area del proyecto (regla de negocio definida desde
    // el diseno original del modelo).
    [HttpPost]
    public async Task<IActionResult> Add(Guid projectId, AddProjectMemberDto dto)
    {
        var userId = GetUserIdFromToken();
        var inviteeId = dto.UserId.ToLowerInvariant(); // ver comentario en User.Id

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound();

        if (project.OwnerId != userId) return Forbid();

        var invitee = await _db.Users.FindAsync(inviteeId);
        if (invitee is null) return BadRequest("Usuario invalido.");

        if (invitee.AreaId != project.AreaId)
            return BadRequest("Solo puedes invitar personas de la misma area del proyecto.");

        var alreadyMember = await _db.ProjectMembers
            .AnyAsync(m => m.ProjectId == projectId && m.UserId == inviteeId);
        if (alreadyMember) return Conflict("El usuario ya es miembro del proyecto.");

        _db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = inviteeId,
            JoinedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();

        return Ok(new ProjectMemberDto(invitee.Id, invitee.FullName, invitee.Email, DateTime.UtcNow, false));
    }

    // DELETE /api/projects/{projectId}/members/{memberUserId}
    // Solo el dueno puede quitar miembros. El dueno no se puede quitar
    // a si mismo por esta via (tendria que transferir o borrar el proyecto).
    [HttpDelete("{memberUserId}")]
    public async Task<IActionResult> Remove(Guid projectId, string memberUserId)
    {
        memberUserId = memberUserId.ToLowerInvariant(); // ver comentario en User.Id
        var userId = GetUserIdFromToken();

        var project = await _db.Projects.FindAsync(projectId);
        if (project is null) return NotFound();

        if (project.OwnerId != userId) return Forbid();
        if (memberUserId == project.OwnerId) return BadRequest("El dueno no puede quitarse a si mismo.");

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == memberUserId);

        if (member is null) return NotFound();

        _db.ProjectMembers.Remove(member);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private string GetUserIdFromToken()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Token sin claim 'sub'.");

        // Normalizado a minusculas - ver comentario en User.Id.
        return sub.ToLowerInvariant();
    }
}