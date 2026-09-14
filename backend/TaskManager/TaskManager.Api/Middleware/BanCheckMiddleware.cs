using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManager.Infrastructure;

namespace TaskManager.Api.Middleware;

// Corta cualquier peticion autenticada de un usuario baneado
// (User.IsBanned). Va despues de UseAuthentication() y antes de
// MapControllers() en Program.cs, asi que ningun controller llega a
// ejecutarse para un usuario bloqueado -no hace falta repetir este
// chequeo en cada uno, a diferencia de la autorizacion de negocio
// (membresia de proyecto, dueno, etc.) que si se valida por endpoint.
//
// Keycloak sigue emitiendo un JWT valido para este usuario (no hay
// forma de revocar eso desde aca), asi que el bloqueo real ocurre aca,
// consultando nuestra propia tabla Users en cada request.
public class BanCheckMiddleware
{
    private readonly RequestDelegate _next;

    public BanCheckMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst("sub")?.Value;

            if (sub is not null && Guid.TryParse(sub, out var userId))
            {
                var isBanned = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.IsBanned)
                    .FirstOrDefaultAsync();

                if (isBanned)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "account_banned",
                        message = "Tu cuenta ha sido bloqueada. Contacta a un administrador.",
                    }));
                    return;
                }
            }
        }

        await _next(context);
    }
}
