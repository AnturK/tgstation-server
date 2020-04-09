﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;
using Z.EntityFramework.Plus;

namespace Tgstation.Server.Host.Controllers
{
	/// <summary>
	/// <see cref="ApiController"/> for managing <see cref="InstanceUser"/>s.
	/// </summary>
	[Route(Routes.InstanceUser)]
	public sealed class InstanceUserController : ApiController
	{
		/// <summary>
		/// Construct a <see cref="UserController"/>
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the <see cref="ApiController"/></param>
		/// <param name="authenticationContextFactory">The <see cref="IAuthenticationContextFactory"/> for the <see cref="ApiController"/></param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="ApiController"/></param>
		public InstanceUserController(IDatabaseContext databaseContext, IAuthenticationContextFactory authenticationContextFactory, ILogger<InstanceUserController> logger) : base(databaseContext, authenticationContextFactory, logger, true, true)
		{ }

		/// <summary>
		/// Checks a <paramref name="model"/> for errors
		/// </summary>
		/// <param name="model">The <see cref="Api.Models.InstanceUser"/> to check</param>
		/// <returns>A <see cref="BadRequestResult"/> explaining any errors, <see langword="null"/> if none</returns>
		BadRequestObjectResult StandardModelChecks(Api.Models.InstanceUser model)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			if (!model.UserId.HasValue)
				return BadRequest(new ErrorMessage { Message = "Missing UserId!" });

			return null;
		}

		/// <summary>
		/// Create am <see cref="Api.Models.InstanceUser"/>.
		/// </summary>
		/// <param name="model">The <see cref="Api.Models.InstanceUser"/> to create.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="201"><see cref="Api.Models.InstanceUser"/> created successfully.</response>
		[HttpPut]
		[TgsAuthorize(InstanceUserRights.CreateUsers)]
		[ProducesResponseType(typeof(Api.Models.InstanceUser), 201)]
		public async Task<IActionResult> Create([FromBody] Api.Models.InstanceUser model, CancellationToken cancellationToken)
		{
			var test = StandardModelChecks(model);
			if (test != null)
				return test;

			var dbUser = new Models.InstanceUser
			{
				ByondRights = model.ByondRights ?? ByondRights.None,
				ChatBotRights = model.ChatBotRights ?? ChatBotRights.None,
				ConfigurationRights = model.ConfigurationRights ?? ConfigurationRights.None,
				DreamDaemonRights = model.DreamDaemonRights ?? DreamDaemonRights.None,
				DreamMakerRights = model.DreamMakerRights ?? DreamMakerRights.None,
				RepositoryRights = model.RepositoryRights ?? RepositoryRights.None,
				InstanceUserRights = model.InstanceUserRights ?? InstanceUserRights.None,
				UserId = model.UserId,
				InstanceId = Instance.Id
			};

			DatabaseContext.InstanceUsers.Add(dbUser);

			await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);
			return StatusCode((int)HttpStatusCode.Created, dbUser.ToApi());
		}

		/// <summary>
		/// Update the permissions for an <see cref="Api.Models.InstanceUser"/>.
		/// </summary>
		/// <param name="model">The updated <see cref="Api.Models.InstanceUser"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200"><see cref="Api.Models.InstanceUser"/> updated successfully.</response>
		/// <response code="410">Instance user unavailable.</response>
		[HttpPost]
		[TgsAuthorize(InstanceUserRights.WriteUsers)]
		[ProducesResponseType(typeof(Api.Models.InstanceUser), 200)]
		[ProducesResponseType(410)]
		#pragma warning disable CA1506 // TODO: Decomplexify
		public async Task<IActionResult> Update([FromBody] Api.Models.InstanceUser model, CancellationToken cancellationToken)
		{
			var test = StandardModelChecks(model);
			if (test != null)
				return test;

			var originalUser = await DatabaseContext.Instances.Where(x => x.Id == Instance.Id).SelectMany(x => x.InstanceUsers).Where(x => x.UserId == model.UserId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (originalUser == null)
				return StatusCode((int)HttpStatusCode.Gone);

			originalUser.ByondRights = model.ByondRights ?? originalUser.ByondRights;
			originalUser.RepositoryRights = model.RepositoryRights ?? originalUser.RepositoryRights;
			originalUser.InstanceUserRights = model.InstanceUserRights ?? originalUser.InstanceUserRights;
			originalUser.ChatBotRights = model.ChatBotRights ?? originalUser.ChatBotRights;
			originalUser.ConfigurationRights = model.ConfigurationRights ?? originalUser.ConfigurationRights;
			originalUser.DreamDaemonRights = model.DreamDaemonRights ?? originalUser.DreamDaemonRights;
			originalUser.DreamMakerRights = model.DreamMakerRights ?? originalUser.DreamMakerRights;

			await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);
			return Json(originalUser.UserId == AuthenticationContext.User.Id || (AuthenticationContext.GetRight(RightsType.InstanceUser) & (ulong)InstanceUserRights.ReadUsers) != 0 ? originalUser.ToApi() : new Api.Models.InstanceUser
			{
				UserId = originalUser.UserId
			});
		}
#pragma warning restore CA1506
		/// <summary>
		/// Read the active <see cref="Api.Models.InstanceUser"/>.
		/// </summary>
		/// <returns>The <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200"><see cref="Api.Models.InstanceUser"/> retrieved successfully.</response>
		[HttpGet]
		[TgsAuthorize]
		[ProducesResponseType(typeof(Api.Models.InstanceUser), 200)]
		public IActionResult Read() => Json(AuthenticationContext.InstanceUser.ToApi());

		/// <summary>
		/// Lists <see cref="Api.Models.InstanceUser"/>s for the instance.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Retrieved <see cref="Api.Models.InstanceUser"/>s successfully.</response>
		[HttpGet(Routes.List)]
		[TgsAuthorize(InstanceUserRights.ReadUsers)]
		[ProducesResponseType(typeof(IEnumerable<Api.Models.InstanceUser>), 200)]
		public async Task<IActionResult> List(CancellationToken cancellationToken)
		{
			var users = await DatabaseContext.Instances.Where(x => x.Id == Instance.Id).SelectMany(x => x.InstanceUsers).ToListAsync(cancellationToken).ConfigureAwait(false);
			return Json(users.Select(x => x.ToApi()));
		}

		/// <summary>
		/// Gets a specific <see cref="Api.Models.InstanceUser"/>.
		/// </summary>
		/// <param name="id">The <see cref="Api.Models.InstanceUser.UserId"/>.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Retrieve <see cref="Api.Models.InstanceUser"/> successfully.</response>
		/// <response code="410">Instance user unavailable.</response>
		[HttpGet("{id}")]
		[TgsAuthorize(InstanceUserRights.ReadUsers)]
		[ProducesResponseType(typeof(Api.Models.InstanceUser), 200)]
		[ProducesResponseType(410)]
		public async Task<IActionResult> GetId(long id, CancellationToken cancellationToken)
		{
			// this functions as userId
			var user = await DatabaseContext.Instances.Where(x => x.Id == Instance.Id).SelectMany(x => x.InstanceUsers).Where(x => x.UserId == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (user == default)
				return StatusCode((int)HttpStatusCode.Gone);
			return Json(user.ToApi());
		}

		/// <summary>
		/// Delete an <see cref="Api.Models.InstanceUser"/>.
		/// </summary>
		/// <param name="id">The <see cref="Api.Models.InstanceUser.UserId"/> to delete.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="204"><see cref="Api.Models.InstanceUser"/> deleted or no longer exists.</response>
		[HttpDelete("{id}")]
		[TgsAuthorize(InstanceUserRights.WriteUsers)]
		[ProducesResponseType(204)]
		public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
		{
			await DatabaseContext.Instances.Where(x => x.Id == Instance.Id).SelectMany(x => x.InstanceUsers).Where(x => x.UserId == id).DeleteAsync(cancellationToken).ConfigureAwait(false);
			return NoContent();
		}
	}
}
