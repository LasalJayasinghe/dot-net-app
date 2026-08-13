using System.Threading.Tasks;

namespace dotnetApp.Application.Interface;

public interface IBrevoEmailService
{
    Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose);
}
