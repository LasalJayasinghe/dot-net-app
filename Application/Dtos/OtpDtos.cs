namespace dotnetApp.Application.Dtos;

public class SendOtpDto
{
    public required string Email { get; set; }
    public required string Purpose { get; set; } // "SignUp" or "ForgotPassword"
}

public class RegisterWithOtpDto
{
    public required string Email { get; set; }
    public required string Password { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public required string OtpCode { get; set; }
}

public class ResetPasswordWithOtpDto
{
    public required string Email { get; set; }
    public required string NewPassword { get; set; }
    public required string OtpCode { get; set; }
}
