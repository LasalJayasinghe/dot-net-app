public class RegisterDto
{
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public class ProfileDto
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string? Bio{ get; set; }

}