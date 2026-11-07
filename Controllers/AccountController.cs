
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CancerCareHub.Data;
using CancerCareHub.Models;
using CancerCareHub.Services;

namespace CancerCareHub.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context,
        IEmailService emailService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _emailService = emailService;
    }

    // GET: Register
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    // POST: Register
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var existingUser = await _userManager.FindByEmailAsync(model.Email);

        if (existingUser != null)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "This email is already registered.");

            return View(model);
        }

        var user = new ApplicationUser
        {
            FullName = model.FullName.Trim(),
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(
            user,
            model.Password
        );

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        // Public registration always creates a Patient.
        var roleResult = await _userManager.AddToRoleAsync(
            user,
            "Patient"
        );

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            ModelState.AddModelError(
                string.Empty,
                "Could not assign the account role.");

            return View(model);
        }

        // Generate a cryptographically secure 6-digit OTP.
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000)
            .ToString();

        var otpRecord = new EmailOtpVerification
        {
            UserId = user.Id,
            CodeHash = HashOtp(otp),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            Attempts = 0,
            IsUsed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.EmailOtpVerifications.Add(otpRecord);
        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendEmailAsync(
                user.Email!,
                "Cancer Care Hub - Email Verification",
                $"Your verification code is {otp}. " +
                "It expires in 5 minutes. Do not share this code."
            );
        }
        catch
        {
            _context.EmailOtpVerifications.Remove(otpRecord);
            await _context.SaveChangesAsync();
            await _userManager.DeleteAsync(user);

            ModelState.AddModelError(
                string.Empty,
                "Email could not be sent. Please try registering again.");

            return View(model);
        }

        return RedirectToAction(
            nameof(VerifyOtp),
            new { email = user.Email }
        );
    }

    // GET: Verify OTP
    [HttpGet]
    public IActionResult VerifyOtp(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return RedirectToAction(nameof(Register));

        return View(new VerifyOtpViewModel
        {
            Email = email
        });
    }

    // POST: Verify OTP
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(
        VerifyOtpViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user == null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid verification request.");

            return View(model);
        }

        if (user.EmailConfirmed)
        {
            return RedirectToAction(nameof(Login));
        }

        var otpRecord = await _context.EmailOtpVerifications
            .Where(x => x.UserId == user.Id && !x.IsUsed)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (otpRecord == null)
        {
            ModelState.AddModelError(
                string.Empty,
                "No active OTP found. Please register again.");

            return View(model);
        }

        if (otpRecord.ExpiresAtUtc < DateTime.UtcNow)
        {
            otpRecord.IsUsed = true;
            await _context.SaveChangesAsync();

            ModelState.AddModelError(
                string.Empty,
                "OTP expired. Please request a new code.");

            return View(model);
        }

        if (otpRecord.Attempts >= 5)
        {
            ModelState.AddModelError(
                string.Empty,
                "Too many attempts. Please request a new code.");

            return View(model);
        }

        otpRecord.Attempts++;

        var submittedHash = HashOtp(model.Code);

        var submittedBytes = Convert.FromHexString(submittedHash);
        var storedBytes = Convert.FromHexString(otpRecord.CodeHash);

        var isCorrect = CryptographicOperations.FixedTimeEquals(
            submittedBytes,
            storedBytes
        );

        if (!isCorrect)
        {
            await _context.SaveChangesAsync();

            ModelState.AddModelError(
                string.Empty,
                "Incorrect OTP.");

            return View(model);
        }

        // Mark OTP as used and confirm the email.
        otpRecord.IsUsed = true;
        user.EmailConfirmed = true;

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            ModelState.AddModelError(
                string.Empty,
                "Could not verify your email. Please try again.");

            return View(model);
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] =
            "Email verified successfully. You can now log in.";

        return RedirectToAction(nameof(Login));
    }

    // GET: Login
    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    // POST: Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Email and password are required.";
            return View();
        }

        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            ViewBag.Error = "Invalid email or password.";
            return View();
        }

        if (!user.EmailConfirmed)
        {
            ViewBag.Error = "Please verify your email first.";
            return View();
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            password,
            isPersistent: false,
            lockoutOnFailure: true
        );

        if (!result.Succeeded)
        {
            ViewBag.Error = "Invalid email or password.";
            return View();
        }

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return RedirectToAction("Admin", "Dashboard");

        if (await _userManager.IsInRoleAsync(user, "Doctor"))
            return RedirectToAction("Doctor", "Dashboard");

        return RedirectToAction("Patient", "Dashboard");
    }

    // POST: Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        return RedirectToAction("Index", "Home");
    }

    private static string HashOtp(string otp)
    {
        var bytes = Encoding.UTF8.GetBytes(otp);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }
}
