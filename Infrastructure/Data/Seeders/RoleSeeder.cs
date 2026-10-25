using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace dotnetApp.Infrastructure.Data.Seeders
{
    public static class RoleSeeder
    {
        public static async Task SeedRolesAndPermissionsAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var dbContext = serviceProvider.GetRequiredService<AppDbContext>();

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

            // 4️⃣ Optional: create a default admin user. The password comes from Seed__AdminPassword
            // rather than the source code - a password in the repo is one anyone can sign in with.
            // When it is not set, no admin account is created.
            var adminPassword = serviceProvider.GetRequiredService<IConfiguration>()["Seed:AdminPassword"];
            var adminUser = await userManager.FindByEmailAsync("admin@example.com");
            if (adminUser == null && !string.IsNullOrWhiteSpace(adminPassword))
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@example.com",
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(adminUser, adminPassword);
                await userManager.AddToRoleAsync(adminUser, "Admin");

                dbContext.Profiles.Add(new Profile
                {
                    UserId = adminUser.Id,
                    FirstName = "Admin",
                    LastName = "User",
                    Bio = "Default admin account"
                });
                
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
