using Microsoft.AspNetCore.Identity;

namespace Cleared.Infrastructure.Identity;

// Lives in Infrastructure, not Domain: IdentityUser is a framework type and Domain stays
// framework-free. TenantId and Role are all this app needs beyond IdentityUser.
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string Role { get; set; } = "Owner";
}
