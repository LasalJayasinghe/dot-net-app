using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

public class TokenService
{
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public TokenService(
        IConfiguration config,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _config = config;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<string> CreateTokenAsync(ApplicationUser user)
    {
        // 🔹 Base claims (identity)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? ""),
            new Claim(ClaimTypes.Email, user.Email ?? "")
        };

        // 🔹 Get roles
        var roles = await _userManager.GetRolesAsync(user);

        foreach (var role in roles)
        {
            // Add role claim
            claims.Add(new Claim(ClaimTypes.Role, role));

            // 🔹 Get permissions from role
            var roleEntity = await _roleManager.FindByNameAsync(role);
            if (roleEntity == null) continue;

            var roleClaims = await _roleManager.GetClaimsAsync(roleEntity);

            foreach (var rc in roleClaims)
            {
                // Avoid duplicates
                if (!claims.Any(c => c.Type == rc.Type && c.Value == rc.Value))
                {
                    claims.Add(new Claim(rc.Type, rc.Value));
                }
            }
        }

        // 🔐 Signing key
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 🎟️ Create token
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}