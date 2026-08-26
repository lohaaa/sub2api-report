using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sub2ApiReport.Application.Security;

namespace Sub2ApiReport.Infrastructure.Identity;

internal sealed class AdministratorClaimsPrincipalFactory(
    UserManager<Administrator> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    TimeProvider timeProvider)
    : UserClaimsPrincipalFactory<Administrator>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Administrator user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(
            SecurityClaimTypes.SessionStartedAt,
            timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        return identity;
    }
}
