using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace dotnetApp.Data.Seeders
{
    public static class RoleSeeder
    {
        public static async Task SeedRolesAndPermissionsAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // 1️⃣ Define roles
            string[] roles = { "Admin", "User" };

            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // 2️⃣ Define permissions
            var allPermissions = new List<string>
            {
                "dashboard.access",
                "alert.create",
                "alert.view",
                "alert.edit"
            };

            // Admin gets all permissions
            var adminRole = await roleManager.FindByNameAsync("Admin");
            if (adminRole != null)
            {
                foreach (var perm in allPermissions)
                {
                    var claims = await roleManager.GetClaimsAsync(adminRole);
                    if (!claims.Any(c => c.Type == "permission" && c.Value == perm))
                    {
                        await roleManager.AddClaimAsync(adminRole, new Claim("permission", perm));
                    }
                }
            }

            // User gets only alert permissions + dashboard
            var userRole = await roleManager.FindByNameAsync("User");
            var userPerms = new List<string> { "dashboard.access", "alert.create", "alert.view", "alert.edit" };

            if (userRole != null)
            {
                foreach (var perm in userPerms)
                {
                    var claims = await roleManager.GetClaimsAsync(userRole);
                    if (!claims.Any(c => c.Type == "permission" && c.Value == perm))
                    {
                        await roleManager.AddClaimAsync(userRole, new Claim("permission", perm));
                    }
                }
            }

            // 4️⃣ Optional: create a default admin user
            var adminUser = await userManager.FindByEmailAsync("admin@example.com");
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@example.com",
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(adminUser, "Admin@123"); // Set default password
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
    }
}
