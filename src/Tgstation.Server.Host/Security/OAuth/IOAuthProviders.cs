using Tgstation.Server.Api.Models;

namespace Tgstation.Server.Host.Security.OAuth
{
	/// <summary>
	/// Contains <see cref="IOAuthValidator"/>s
	/// </summary>
	public interface IOAuthProviders
	{
		/// <summary>
		/// Gets the <see cref="IOAuthValidator"/> for a given <paramref name="oAuthProvider"/>.
		/// </summary>
		/// <param name="oAuthProvider">The <see cref="OAuthProvider"/> to get the validator for.</param>
		/// <returns>The <see cref="IOAuthValidator"/> for <paramref name="oAuthProvider"/>.</returns>
		IOAuthValidator GetValidator(OAuthProvider oAuthProvider);
	}
}
