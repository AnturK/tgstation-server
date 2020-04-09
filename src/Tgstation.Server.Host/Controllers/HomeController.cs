﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;
using Wangkanai.Detection;

namespace Tgstation.Server.Host.Controllers
{
	/// <summary>
	/// Main <see cref="ApiController"/> for the <see cref="Application"/>
	/// </summary>
	[Route(Routes.Root)]
	public sealed class HomeController : ApiController
	{
		/// <summary>
		/// The <see cref="ITokenFactory"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly ITokenFactory tokenFactory;

		/// <summary>
		/// The <see cref="ISystemIdentityFactory"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly ISystemIdentityFactory systemIdentityFactory;

		/// <summary>
		/// The <see cref="ICryptographySuite"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly ICryptographySuite cryptographySuite;

		/// <summary>
		/// The <see cref="IApplication"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly IApplication application;

		/// <summary>
		/// The <see cref="IIdentityCache"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly IIdentityCache identityCache;

		/// <summary>
		/// The <see cref="IBrowserResolver"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly IBrowserResolver browserResolver;

		/// <summary>
		/// The <see cref="ControlPanelConfiguration"/> for the <see cref="HomeController"/>
		/// </summary>
		readonly ControlPanelConfiguration controlPanelConfiguration;

		/// <summary>
		/// Construct a <see cref="HomeController"/>
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the <see cref="ApiController"/></param>
		/// <param name="authenticationContextFactory">The <see cref="IAuthenticationContextFactory"/> for the <see cref="ApiController"/></param>
		/// <param name="tokenFactory">The value of <see cref="tokenFactory"/></param>
		/// <param name="systemIdentityFactory">The value of <see cref="systemIdentityFactory"/></param>
		/// <param name="cryptographySuite">The value of <see cref="cryptographySuite"/></param>
		/// <param name="application">The value of <see cref="application"/></param>
		/// <param name="identityCache">The value of <see cref="identityCache"/></param>
		/// <param name="browserResolver">The value of <see cref="browserResolver"/></param>
		/// <param name="controlPanelConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="controlPanelConfiguration"/></param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="ApiController"/></param>
		public HomeController(IDatabaseContext databaseContext, IAuthenticationContextFactory authenticationContextFactory, ITokenFactory tokenFactory, ISystemIdentityFactory systemIdentityFactory, ICryptographySuite cryptographySuite, IApplication application, IIdentityCache identityCache, IBrowserResolver browserResolver, IOptions<ControlPanelConfiguration> controlPanelConfigurationOptions, ILogger<HomeController> logger) : base(databaseContext, authenticationContextFactory, logger, false, false)
		{
			this.tokenFactory = tokenFactory ?? throw new ArgumentNullException(nameof(tokenFactory));
			this.systemIdentityFactory = systemIdentityFactory ?? throw new ArgumentNullException(nameof(systemIdentityFactory));
			this.cryptographySuite = cryptographySuite ?? throw new ArgumentNullException(nameof(cryptographySuite));
			this.application = application ?? throw new ArgumentNullException(nameof(application));
			this.identityCache = identityCache ?? throw new ArgumentNullException(nameof(identityCache));
			this.browserResolver = browserResolver ?? throw new ArgumentNullException(nameof(browserResolver));
			controlPanelConfiguration = controlPanelConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(controlPanelConfigurationOptions));
		}

		/// <summary>
		/// Main page of the <see cref="Application"/>
		/// </summary>
		/// <returns>
		/// The <see cref="Api.Models.ServerInformation"/> of the <see cref="Application"/> if a properly authenticated API request, the web control panel if on a browser and enabled, <see cref="UnauthorizedResult"/> otherwise.
		/// </returns>
		/// <response code="200"><see cref="Api.Models.ServerInformation"/> retrieved successfully.</response>
		[HttpGet]
		[TgsAuthorize]
		[AllowAnonymous]
		[ProducesResponseType(typeof(Api.Models.ServerInformation), 200)]
		public IActionResult Home()
		{
			if (AuthenticationContext != null)
				return Json(new Api.Models.ServerInformation
				{
					Version = application.Version,
					ApiVersion = ApiHeaders.Version
				});

			// if we are using a browser and the control panel, soft redirect to the app page
			if (controlPanelConfiguration.Enable && browserResolver.Browser.Type != BrowserType.Generic)
			{
				Logger.LogDebug("Unauthorized browser request (User-Agent: \"{0}\"), loading control panel...", browserResolver.UserAgent);
				return File("~/index.html", MediaTypeNames.Text.Html);
			}

			return Unauthorized();
		}

		/// <summary>
		/// Attempt to authenticate a <see cref="User"/> using <see cref="ApiController.ApiHeaders"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the operation</returns>
		/// <response code="200">User logged in and <see cref="Api.Models.Token"/> generated successfully.</response>
		/// <response code="401">User authentication failed.</response>
		/// <response code="403">User authenticated but is disabled by an administrator.</response>
		[HttpPost]
		[ProducesResponseType(typeof(Api.Models.Token), 200)]
		[ProducesResponseType(401)]
		[ProducesResponseType(403)]
		#pragma warning disable CA1506 // TODO: Decomplexify
		public async Task<IActionResult> CreateToken(CancellationToken cancellationToken)
		{
			if (ApiHeaders == null)
			{
				// Get the exact error
				var errorMessage = "Missing API headers!";
				try
				{
					var _ = new ApiHeaders(Request.GetTypedHeaders());
				}
				catch (InvalidOperationException ex)
				{
					errorMessage = ex.Message;
				}

				Response.Headers.Add(HeaderNames.WWWAuthenticate, new StringValues("basic realm=\"Create TGS4 bearer token\""));

				return BadRequest(new Api.Models.ErrorMessage { Message = errorMessage });
			}

			if (ApiHeaders.IsTokenAuthentication)
				return BadRequest(new Api.Models.ErrorMessage { Message = "Cannot create a token using another token!" });

			ISystemIdentity systemIdentity;
			try
			{
				// trust the system over the database because a user's name can change while still having the same SID
				systemIdentity = await systemIdentityFactory.CreateSystemIdentity(ApiHeaders.Username, ApiHeaders.Password, cancellationToken).ConfigureAwait(false);
			}
			catch (NotImplementedException)
			{
				systemIdentity = null;
			}

			using (systemIdentity)
			{
				// Get the user from the database
				IQueryable<User> query;
				string canonicalName = Models.User.CanonicalizeName(ApiHeaders.Username);
				if (systemIdentity == null)
					query = DatabaseContext.Users.Where(x => x.CanonicalName == canonicalName);
				else
					query = DatabaseContext.Users.Where(x => x.CanonicalName == canonicalName || x.SystemIdentifier == systemIdentity.Uid);
				var users = await query.Select(x => new User
				{
					Id = x.Id,
					PasswordHash = x.PasswordHash,
					Enabled = x.Enabled,
					Name = x.Name
				}).ToListAsync(cancellationToken).ConfigureAwait(false);

				// Pick the DB user first
				var user = users
					.OrderByDescending(dbUser => dbUser.PasswordHash != null)
					.FirstOrDefault();

				// No user? You're not allowed
				if (user == null)
					return Unauthorized();

				// A system user may have had their name AND password changed to one in our DB...
				// Or a DB user was created that had the same user/pass as a system user
				// Dumb admins...
				// FALLBACK TO THE DB USER HERE, DO NOT REVEAL A SYSTEM LOGIN!!!
				// This of course, allows system users to discover TGS users in this (HIGHLY IMPROBABLE) case but that is not our fault
				var originalHash = user.PasswordHash;
				var isDbUser = originalHash != null;
				bool usingSystemIdentity = systemIdentity != null && !isDbUser;
				if (!usingSystemIdentity)
				{
					// DB User password check and update
					if (!cryptographySuite.CheckUserPassword(user, ApiHeaders.Password))
						return Unauthorized();
					if (user.PasswordHash != originalHash)
					{
						Logger.LogDebug("User ID {0}'s password hash needs a refresh, updating database.", user.Id);
						var updatedUser = new User
						{
							Id = user.Id
						};
						DatabaseContext.Users.Attach(updatedUser);
						updatedUser.PasswordHash = user.PasswordHash;
						await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);
					}
				}
				else if (systemIdentity.Username != user.Name)
				{
					// System identity username change update
					Logger.LogDebug("User ID {0}'s system identity needs a refresh, updating database.", user.Id);
					DatabaseContext.Users.Attach(user);
					user.Name = systemIdentity.Username;
					user.CanonicalName = Models.User.CanonicalizeName(user.Name);
					await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);
				}

				// Now that the bookeeping is done, tell them to fuck off if necessary
				if (!user.Enabled.Value)
				{
					Logger.LogTrace("Not logging in disabled user {0}.", user.Id);
					return Forbid();
				}

				var token = await tokenFactory.CreateToken(user, cancellationToken).ConfigureAwait(false);
				if (usingSystemIdentity)
				{
					// expire the identity slightly after the auth token in case of lag
					var identExpiry = token.ExpiresAt.Value;
					identExpiry += tokenFactory.ValidationParameters.ClockSkew;
					identExpiry += TimeSpan.FromSeconds(15);
					identityCache.CacheSystemIdentity(user, systemIdentity, identExpiry);
				}

				Logger.LogDebug("Successfully logged in user {0}!", user.Id);

				return Json(token);
			}
		}
		#pragma warning restore CA1506
	}
}
