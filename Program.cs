
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CancerCareHub.Data;
using CancerCareHub.Models;
using CancerCareHub.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. MVC
builder.Services.AddControllersWithViews();

// 2. MySQL Database
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    )
);

// 3. ASP.NET Core Identity
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Patient email OTP verification is required.
        // The seeded Admin account is automatically confirmed.
        options.SignIn.RequireConfirmedEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(10);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// 4. Email Service
builder.Services.AddScoped<IEmailService, EmailService>();

// 5. Authentication Cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Home/Privacy";

    options.Cookie.HttpOnly = true;
    options.Cookie.Name = "CancerCareHub.Auth";

    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

var app = builder.Build();

// 6. Create Roles and Admin Account
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var roleManager = services
        .GetRequiredService<RoleManager<IdentityRole>>();

    var userManager = services
        .GetRequiredService<UserManager<ApplicationUser>>();

    // Create roles if they do not exist.
    string[] roles = { "Admin", "Doctor", "Patient" };

    foreach (var roleName in roles)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    $"Could not create role {roleName}: {errors}");
            }
        }
    }

    // Read Admin credentials from User Secrets.
    var adminEmail =
        builder.Configuration["AdminAccount:Email"]?.Trim();

    var adminPassword =
        builder.Configuration["AdminAccount:Password"];

    var adminFullName =
        builder.Configuration["AdminAccount:FullName"]?.Trim();

    if (string.IsNullOrWhiteSpace(adminEmail) ||
        string.IsNullOrWhiteSpace(adminPassword))
    {
        app.Logger.LogWarning(
            "Admin credentials are missing. Admin account was not created.");
    }
    else
    {
        // Find existing Admin account.
        var adminUser =
            await userManager.FindByEmailAsync(adminEmail);

        // Create Admin account if it does not exist.
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = string.IsNullOrWhiteSpace(adminFullName)
                    ? "Cancer Care Hub Administrator"
                    : adminFullName,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(
                adminUser,
                adminPassword);

            if (!createResult.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    createResult.Errors.Select(e => e.Description));

                throw new InvalidOperationException(
                    "Admin creation failed: " + errors);
            }
        }
        else
        {
            // Ensure Admin email is confirmed.
            var changed = false;

            if (!adminUser.EmailConfirmed)
            {
                adminUser.EmailConfirmed = true;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(adminUser.FullName) &&
                !string.IsNullOrWhiteSpace(adminFullName))
            {
                adminUser.FullName = adminFullName;
                changed = true;
            }

            if (changed)
            {
                var updateResult =
                    await userManager.UpdateAsync(adminUser);

                if (!updateResult.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        updateResult.Errors.Select(e => e.Description));

                    throw new InvalidOperationException(
                        "Admin update failed: " + errors);
                }
            }
        }

        // Remove Patient/Doctor roles from this Admin account.
        var currentRoles =
            await userManager.GetRolesAsync(adminUser);

        var otherRoles = currentRoles
            .Where(role => !string.Equals(
                role,
                "Admin",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (otherRoles.Length > 0)
        {
            var removeResult =
                await userManager.RemoveFromRolesAsync(
                    adminUser,
                    otherRoles);

            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not remove other roles from Admin account.");
            }
        }

        // Assign Admin role.
        if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            var roleResult =
                await userManager.AddToRoleAsync(adminUser, "Admin");

            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not assign Admin role.");
            }
        }

        app.Logger.LogInformation(
            "Admin account is ready: {Email}", adminEmail);
    }
}

// 7. HTTP Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
