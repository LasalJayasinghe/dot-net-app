using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace dotnetApp.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;

    public AuthController(UserManager<ApplicationUser> userManager, TokenService tokenService)
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto model)
    {
        var user = await _userManager.FindByNameAsync(model.Username);

        if (user == null)
            return Unauthorized("Invalid credentials");

        var passwordValid = await _userManager.CheckPasswordAsync(user, model.Password);

        if (!passwordValid)
            return Unauthorized("Invalid credentials");

        var token = _tokenService.CreateToken(user);

        return Ok(new
        {
            token,
            username = user.UserName
        });
    }
}