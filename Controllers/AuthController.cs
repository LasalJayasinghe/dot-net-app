using dotnetApp.ViewModels.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;


public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FullName = dto.FullName
        };

        var result = await _userManager.CreateAsync(user, dto.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok("User created");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {

        Console.WriteLine("Login attempt for: " + model.Password);
        var result = await _signInManager.PasswordSignInAsync(
            model.Username,
            model.Password,
            false,
            false
        );

        Console.WriteLine("Login result: " + result.Succeeded);

        if (!result.Succeeded)
            return Unauthorized("Invalid login");

        return Ok("Logged in");
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }
}
