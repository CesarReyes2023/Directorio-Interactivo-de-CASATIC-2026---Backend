using System.Security.Claims;
using CasaticDirectorio.Api.DTOs.Auth;
using CasaticDirectorio.Api.Services;
using CasaticDirectorio.Domain.Enums;
using CasaticDirectorio.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CasaticDirectorio.Api.Controllers;

/// <summary>
/// Autenticación: login JWT y cambio de contraseña (primer login).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUsuarioRepository _usuarios;
    private readonly IJwtService _jwt;
    private readonly ILogService _logService;

    public AuthController(IUsuarioRepository usuarios, IJwtService jwt, ILogService logService)
    {
        _usuarios = usuarios;
        _jwt = jwt;
        _logService = logService;
    }

    /// <summary>
    /// Iniciar sesión. Retorna JWT y flag de primer login.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var usuario = await _usuarios.GetByEmailAsync(req.Email);
        if (usuario == null || !usuario.Activo)
            return Unauthorized(new { message = "Credenciales inválidas" });

        if (!BCrypt.Net.BCrypt.Verify(req.Password, usuario.PasswordHash))
            return Unauthorized(new { message = "Credenciales inválidas" });

        var token = _jwt.GenerateToken(usuario);

        // Registrar login en logs
        await _logService.RegistrarAsync(
            TipoEvento.Login,
            usuarioId: usuario.Id,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: Request.Headers.UserAgent.ToString());

        return Ok(new LoginResponse
        {
            Token = token,
            Email = usuario.Email,
            Rol = usuario.Rol.ToString(),
            PrimerLogin = usuario.PrimerLogin,
            SocioId = usuario.SocioId
        });
    }

    /// <summary>
    /// Cambiar contraseña (obligatorio en primer login).
    /// </summary>
    [Authorize]
    [HttpPost("cambiar-password")]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var usuario = await _usuarios.GetByIdAsync(Guid.Parse(userId));
        if (usuario == null) return NotFound();

        // Validación de seguridad de contraseña
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.NuevaPassword, @"^(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,}$"))
            return BadRequest(new { message = "La contraseña debe tener al menos 8 caracteres, una mayúscula, un número y un carácter especial." });

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NuevaPassword);
        usuario.PrimerLogin = false;
        await _usuarios.UpdateAsync(usuario);

        await _logService.RegistrarAsync(TipoEvento.CambioPassword, usuarioId: usuario.Id);

        // Generar nuevo token sin flag primer_login
        var token = _jwt.GenerateToken(usuario);

        return Ok(new { message = "Contraseña actualizada", token });
    }

    /// <summary>
    /// Obtener perfil del usuario autenticado.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var usuario = await _usuarios.GetByIdAsync(Guid.Parse(userId));
        if (usuario == null) return NotFound();

        return Ok(new
        {
            usuario.Id,
            usuario.Email,
            Rol = usuario.Rol.ToString(),
            usuario.PrimerLogin,
            usuario.SocioId
        });
    }
}
