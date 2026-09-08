using Microsoft.AspNetCore.Identity;

namespace Cleared.Infrastructure.Identity;

// Deliberately lives in Infrastructure, not Domain — IdentityUser is a framework type,
// and Domain must stay framework-free. TenantId + Role are the only things this app
// actually needs beyond what IdentityUser already provides.
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string Role { get; set; } = "Owner";
}
