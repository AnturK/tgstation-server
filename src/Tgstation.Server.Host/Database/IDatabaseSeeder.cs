﻿using System.Threading;
using System.Threading.Tasks;

namespace Tgstation.Server.Host.Database
{
	/// <summary>
	/// For initially seeding a database
	/// </summary>
	interface IDatabaseSeeder
	{
		/// <summary>
		/// Initially seed a given <paramref name="databaseContext"/>
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> to seed</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task SeedDatabase(IDatabaseContext databaseContext, CancellationToken cancellationToken);

		/// <summary>
		/// Correct invalid database data caused by previous versions.
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> to sanitize.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task"/> representing the running operation.</returns>
		Task SanitizeDatabase(IDatabaseContext databaseContext, CancellationToken cancellationToken);

		/// <summary>
		/// Changes the admin password in <see cref="IDatabaseContext"/> back to it's default and enables the account
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> to reset the admin password for</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task ResetAdminPassword(IDatabaseContext databaseContext, CancellationToken cancellationToken);
	}
}
