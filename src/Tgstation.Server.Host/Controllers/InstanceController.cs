﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Host.Components;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Models;
using Tgstation.Server.Host.Security;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Controllers
{
	/// <summary>
	/// <see cref="ApiController"/> for managing <see cref="Components.Instance"/>s
	/// </summary>
	[Route(Routes.InstanceManager)]
	#pragma warning disable CA1506 // TODO: Decomplexify
	public sealed class InstanceController : ApiController
	{
		/// <summary>
		/// File name to allow attaching instances
		/// </summary>
		const string InstanceAttachFileName = "TGS4_ALLOW_INSTANCE_ATTACH";

		const string MoveInstanceJobPrefix = "Move instance ID ";

		/// <summary>
		/// The <see cref="IJobManager"/> for the <see cref="InstanceController"/>
		/// </summary>
		readonly IJobManager jobManager;

		/// <summary>
		/// The <see cref="IInstanceManager"/> for the <see cref="InstanceController"/>
		/// </summary>
		readonly IInstanceManager instanceManager;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="InstanceController"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IApplication"/> for the <see cref="InstanceController"/>
		/// </summary>
		readonly IApplication application;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="InstanceController"/>
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// Construct a <see cref="InstanceController"/>
		/// </summary>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the <see cref="ApiController"/></param>
		/// <param name="authenticationContextFactory">The <see cref="IAuthenticationContextFactory"/> for the <see cref="ApiController"/></param>
		/// <param name="jobManager">The value of <see cref="jobManager"/></param>
		/// <param name="instanceManager">The value of <see cref="instanceManager"/></param>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="application">The value of <see cref="application"/></param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/></param>
		/// <param name="logger">The <see cref="ILogger"/> for the <see cref="ApiController"/></param>
		public InstanceController(IDatabaseContext databaseContext, IAuthenticationContextFactory authenticationContextFactory, IJobManager jobManager, IInstanceManager instanceManager, IIOManager ioManager, IApplication application, IPlatformIdentifier platformIdentifier, ILogger<InstanceController> logger) : base(databaseContext, authenticationContextFactory, logger, false, true)
		{
			this.jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
			this.instanceManager = instanceManager ?? throw new ArgumentNullException(nameof(instanceManager));
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.application = application ?? throw new ArgumentNullException(nameof(application));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
		}

		void NormalizeModelPath(Api.Models.Instance model, out string absolutePath)
		{
			if (model.Path == null)
			{
				absolutePath = null;
				return;
			}

			absolutePath = ioManager.ResolvePath(model.Path);
			if (platformIdentifier.IsWindows)
				model.Path = absolutePath.ToUpperInvariant();
			else
				model.Path = absolutePath;
		}

		Models.InstanceUser InstanceAdminUser() => new Models.InstanceUser
		{
			ByondRights = (ByondRights)~0U,
			ChatBotRights = (ChatBotRights)~0U,
			ConfigurationRights = (ConfigurationRights)~0U,
			DreamDaemonRights = (DreamDaemonRights)~0U,
			DreamMakerRights = (DreamMakerRights)~0U,
			RepositoryRights = (RepositoryRights)~0U,
			InstanceUserRights = (InstanceUserRights)~0U,
			UserId = AuthenticationContext.User.Id
		};

		/// <summary>
		/// Create or attach an <see cref="Api.Models.Instance"/>.
		/// </summary>
		/// <param name="model">The <see cref="Api.Models.Instance"/> settings.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Instance attached successfully.</response>
		/// <response code="201">Instance created successfully.</response>
		[HttpPut]
		[TgsAuthorize(InstanceManagerRights.Create)]
		[ProducesResponseType(typeof(Api.Models.Instance), 200)]
		[ProducesResponseType(typeof(Api.Models.Instance), 201)]
		public async Task<IActionResult> Create([FromBody] Api.Models.Instance model, CancellationToken cancellationToken)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			if (String.IsNullOrWhiteSpace(model.Name))
				return BadRequest(new ErrorMessage { Message = "name must not be empty!" });

			if (model.Path == null)
				return BadRequest(new ErrorMessage { Message = "path must not be empty!" });

			NormalizeModelPath(model, out var rawPath);

			var localPath = ioManager.ResolvePath(".");
			NormalizeModelPath(new Api.Models.Instance
			{
				Path = localPath
			}, out var normalizedLocalPath);

			if (rawPath.StartsWith(normalizedLocalPath, StringComparison.Ordinal))
			{
				bool sameLength = rawPath.Length == normalizedLocalPath.Length;
				char dirSeparatorChar = rawPath.ToCharArray()[normalizedLocalPath.Length];
				if(sameLength
					|| dirSeparatorChar == Path.DirectorySeparatorChar
					|| dirSeparatorChar == Path.AltDirectorySeparatorChar)
					return Conflict(new ErrorMessage { Message = "Instances cannot be created in the installation directory!" });
			}

			var dirExistsTask = ioManager.DirectoryExists(model.Path, cancellationToken);
			bool attached = false;
			if (await ioManager.FileExists(model.Path, cancellationToken).ConfigureAwait(false) || await dirExistsTask.ConfigureAwait(false))
				if (!await ioManager.FileExists(ioManager.ConcatPath(model.Path, InstanceAttachFileName), cancellationToken).ConfigureAwait(false))
					return Conflict(new ErrorMessage { Message = "Path not empty!" });
				else
					attached = true;

			var newInstance = new Models.Instance
			{
				ConfigurationType = model.ConfigurationType ?? ConfigurationType.Disallowed,
				DreamDaemonSettings = new DreamDaemonSettings
				{
					AllowWebClient = false,
					AutoStart = false,
					PrimaryPort = 1337,
					SecondaryPort = 1338,
					SecurityLevel = DreamDaemonSecurity.Safe,
					SoftRestart = false,
					SoftShutdown = false,
					StartupTimeout = 20
				},
				DreamMakerSettings = new DreamMakerSettings
				{
					ApiValidationPort = 1339,
					ApiValidationSecurityLevel = DreamDaemonSecurity.Safe
				},
				Name = model.Name,
				Online = false,
				Path = model.Path,
				AutoUpdateInterval = model.AutoUpdateInterval ?? 0,
				RepositorySettings = new RepositorySettings
				{
					CommitterEmail = "tgstation-server@users.noreply.github.com",
					CommitterName = application.VersionPrefix,
					PushTestMergeCommits = false,
					ShowTestMergeCommitters = false,
					AutoUpdatesKeepTestMerges = false,
					AutoUpdatesSynchronize = false,
					PostTestMergeComment = false
				},
				InstanceUsers = new List<Models.InstanceUser> // give this user full privileges on the instance
				{
					InstanceAdminUser()
				}
			};

			DatabaseContext.Instances.Add(newInstance);
			try
			{
				await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);

				try
				{
					// actually reserve it now
					await ioManager.CreateDirectory(rawPath, cancellationToken).ConfigureAwait(false);
					await ioManager.DeleteFile(ioManager.ConcatPath(rawPath, InstanceAttachFileName), cancellationToken).ConfigureAwait(false);
				}
				catch
				{
					// oh shit delete the model
					DatabaseContext.Instances.Remove(newInstance);

					await DatabaseContext.Save(default).ConfigureAwait(false);
					throw;
				}
			}
			catch (IOException e)
			{
				return Conflict(new ErrorMessage { Message = e.Message });
			}
			catch (DbUpdateException e)
			{
				return Conflict(new ErrorMessage { Message = e.Message });
			}

			Logger.LogInformation("{0} {1} instance {2}: {3} ({4})", AuthenticationContext.User.Name, attached ? "attached" : "created", newInstance.Name, newInstance.Id, newInstance.Path);

			var api = newInstance.ToApi();
			return attached ? (IActionResult)Json(api) : StatusCode((int)HttpStatusCode.Created, api);
		}

		/// <summary>
		/// Detach an <see cref="Api.Models.Instance"/> with the given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">The <see cref="Api.Models.Instance.Id"/> to detach.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="204">Instance detatched successfully.</response>
		/// <response code="410">Instance not available.</response>
		[HttpDelete("{id}")]
		[TgsAuthorize(InstanceManagerRights.Delete)]
		[ProducesResponseType(204)]
		[ProducesResponseType(410)]
		public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
		{
			var originalModel = await DatabaseContext.Instances.Where(x => x.Id == id)
				.Include(x => x.WatchdogReattachInformation)
				.Include(x => x.WatchdogReattachInformation.Alpha)
				.Include(x => x.WatchdogReattachInformation.Bravo)
				.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (originalModel == default)
				return StatusCode((int)HttpStatusCode.Gone);
			if (originalModel.Online.Value)
				return Conflict(new ErrorMessage
				{
					Message = "Cannot detach an online instance!"
				});

			if (originalModel.WatchdogReattachInformation != null)
			{
				DatabaseContext.WatchdogReattachInformations.Remove(originalModel.WatchdogReattachInformation);
				if (originalModel.WatchdogReattachInformation.Alpha != null)
					DatabaseContext.ReattachInformations.Remove(originalModel.WatchdogReattachInformation.Alpha);
				if (originalModel.WatchdogReattachInformation.Bravo != null)
					DatabaseContext.ReattachInformations.Remove(originalModel.WatchdogReattachInformation.Bravo);
			}

			DatabaseContext.Instances.Remove(originalModel);

			var attachFileName = ioManager.ConcatPath(originalModel.Path, InstanceAttachFileName);
			await ioManager.WriteAllBytes(attachFileName, Array.Empty<byte>(), default).ConfigureAwait(false);
			await DatabaseContext.Save(cancellationToken).ConfigureAwait(false); // cascades everything
			return NoContent();
		}

		/// <summary>
		/// Modify an <see cref="Api.Models.Instance"/>'s settings.
		/// </summary>
		/// <param name="model">The updated <see cref="Api.Models.Instance"/> settings.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Instance updated successfully.</response>
		/// <response code="202">Instance updated successfully and relocation job created.</response>
		[HttpPost]
		[TgsAuthorize(InstanceManagerRights.Relocate | InstanceManagerRights.Rename | InstanceManagerRights.SetAutoUpdate | InstanceManagerRights.SetConfiguration | InstanceManagerRights.SetOnline)]
		[ProducesResponseType(typeof(Api.Models.Instance), 200)]
		[ProducesResponseType(typeof(Api.Models.Instance), 202)]
		[ProducesResponseType(410)]
#pragma warning disable CA1502 // TODO: Decomplexify
		public async Task<IActionResult> Update([FromBody] Api.Models.Instance model, CancellationToken cancellationToken)
		{
			if (model == null)
				throw new ArgumentNullException(nameof(model));

			var instanceQuery = DatabaseContext.Instances.Where(x => x.Id == model.Id);

			var moveJob = await instanceQuery
				.SelectMany(x => x.Jobs).
#pragma warning disable CA1307 // Specify StringComparison
				Where(x => !x.StoppedAt.HasValue && x.Description.StartsWith(MoveInstanceJobPrefix))
#pragma warning restore CA1307 // Specify StringComparison
				.Select(x => new Models.Job
				{
					Id = x.Id
				}).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

			if (moveJob != default)
				await jobManager.CancelJob(moveJob, AuthenticationContext.User, true, cancellationToken).ConfigureAwait(false); // cancel it now

			var usersInstanceUserTask = instanceQuery.SelectMany(x => x.InstanceUsers).Where(x => x.UserId == AuthenticationContext.User.Id).FirstOrDefaultAsync(cancellationToken);

			var originalModel = await instanceQuery
				.Include(x => x.RepositorySettings)
				.Include(x => x.ChatSettings)
				.ThenInclude(x => x.Channels)
				.Include(x => x.DreamDaemonSettings) // need these for onlining
				.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			if (originalModel == default(Models.Instance))
				return StatusCode((int)HttpStatusCode.Gone);

			var userRights = (InstanceManagerRights)AuthenticationContext.GetRight(RightsType.InstanceManager);
			bool CheckModified<T>(Expression<Func<Api.Models.Instance, T>> expression, InstanceManagerRights requiredRight)
			{
				var memberSelectorExpression = (MemberExpression)expression.Body;
				var property = (PropertyInfo)memberSelectorExpression.Member;

				var newVal = property.GetValue(model);
				if (newVal == null)
					return false;
				if (!userRights.HasFlag(requiredRight) && property.GetValue(originalModel) != newVal)
					return true;

				property.SetValue(originalModel, newVal);
				return false;
			}

			string originalModelPath = null;
			string rawPath = null;
			if (model.Path != null)
			{
				NormalizeModelPath(model, out rawPath);

				if (model.Path != originalModel.Path)
				{
					if (!userRights.HasFlag(InstanceManagerRights.Relocate))
						return Forbid();
					if (originalModel.Online.Value && model.Online != true)
						return Conflict(new ErrorMessage { Message = "Cannot relocate an online instance!" });

					var dirExistsTask = ioManager.DirectoryExists(model.Path, cancellationToken);
					if (await ioManager.FileExists(model.Path, cancellationToken).ConfigureAwait(false) || await dirExistsTask.ConfigureAwait(false))
						return Conflict(new ErrorMessage { Message = "Path not empty!" });

					originalModelPath = originalModel.Path;
					originalModel.Path = model.Path;
				}
			}

			var oldAutoUpdateInterval = originalModel.AutoUpdateInterval.Value;
			var originalOnline = originalModel.Online.Value;
			var renamed = model.Name != null && originalModel.Name != model.Name;

			if (CheckModified(x => x.AutoUpdateInterval, InstanceManagerRights.SetAutoUpdate)
				|| CheckModified(x => x.ConfigurationType, InstanceManagerRights.SetConfiguration)
				|| CheckModified(x => x.Name, InstanceManagerRights.Rename)
				|| CheckModified(x => x.Online, InstanceManagerRights.SetOnline))
				return Forbid();

			// ensure the current user has write privilege on the instance
			var usersInstanceUser = await usersInstanceUserTask.ConfigureAwait(false);
			if (usersInstanceUser == default)
			{
				var instanceAdminUser = InstanceAdminUser();
				instanceAdminUser.InstanceId = originalModel.Id;
				DatabaseContext.InstanceUsers.Add(instanceAdminUser);
			}
			else
				usersInstanceUser.InstanceUserRights |= InstanceUserRights.WriteUsers;

			await DatabaseContext.Save(cancellationToken).ConfigureAwait(false);

			if (renamed)
				instanceManager.GetInstance(originalModel).Rename(originalModel.Name);

			var oldAutoStart = originalModel.DreamDaemonSettings.AutoStart;
			try
			{
				if (originalOnline && model.Online == false)
					await instanceManager.OfflineInstance(originalModel, AuthenticationContext.User, cancellationToken).ConfigureAwait(false);
				else if (!originalOnline && model.Online == true)
				{
					// force autostart false here because we don't want any long running jobs right now
					// remember to document this
					originalModel.DreamDaemonSettings.AutoStart = false;
					await instanceManager.OnlineInstance(originalModel, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception e)
			{
				if(!(e is OperationCanceledException))
					Logger.LogError("Error changing instance online state! Exception: {0}", e);
				originalModel.Online = originalOnline;
				originalModel.DreamDaemonSettings.AutoStart = oldAutoStart;
				if (originalModelPath != null)
					originalModel.Path = originalModelPath;
				await DatabaseContext.Save(default).ConfigureAwait(false);
				throw;
			}

			var api = (AuthenticationContext.GetRight(RightsType.InstanceManager) & (ulong)InstanceManagerRights.Read) != 0 ? originalModel.ToApi() : new Api.Models.Instance
			{
				Id = originalModel.Id
			};

			var moving = originalModelPath != null;
			if (moving)
			{
				var job = new Models.Job
				{
					Description = String.Format(CultureInfo.InvariantCulture, MoveInstanceJobPrefix + "{0} from {1} to {2}", originalModel.Id, originalModel.Path, rawPath),
					Instance = originalModel,
					CancelRightsType = RightsType.InstanceManager,
					CancelRight = (ulong)InstanceManagerRights.Relocate,
					StartedBy = AuthenticationContext.User
				};

				await jobManager.RegisterOperation(job, (paramJob, databaseContext, progressHandler, ct) => instanceManager.MoveInstance(originalModel, rawPath, ct), cancellationToken).ConfigureAwait(false);
				api.MoveJob = job.ToApi();
			}

			if (originalModel.Online.Value && model.AutoUpdateInterval.HasValue && oldAutoUpdateInterval != model.AutoUpdateInterval)
				await instanceManager.GetInstance(originalModel).SetAutoUpdateInterval(model.AutoUpdateInterval.Value).ConfigureAwait(false);

			return moving ? (IActionResult)Accepted(api) : Json(api);
		}
#pragma warning restore CA1502

		/// <summary>
		/// List <see cref="Api.Models.Instance"/>s.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Retrieved <see cref="Api.Models.Instance"/>s successfully.</response>
		[HttpGet(Routes.List)]
		[TgsAuthorize(InstanceManagerRights.List | InstanceManagerRights.Read)]
		[ProducesResponseType(typeof(IEnumerable<Api.Models.Instance>), 200)]
		public async Task<IActionResult> List(CancellationToken cancellationToken)
		{
			IQueryable<Models.Instance> query = DatabaseContext.Instances;
			if (!AuthenticationContext.User.InstanceManagerRights.Value.HasFlag(InstanceManagerRights.List))
				query = query.Where(x => x.InstanceUsers.Any(y => y.UserId == AuthenticationContext.User.Id)).Where(x => x.InstanceUsers.Any(y => y.AnyRights));

			var moveJobTasks = query
				.SelectMany(x => x.Jobs)
#pragma warning disable CA1307 // Specify StringComparison
				.Where(x => !x.StoppedAt.HasValue && x.Description.StartsWith(MoveInstanceJobPrefix))
#pragma warning restore CA1307 // Specify StringComparison
				.Include(x => x.StartedBy).ThenInclude(x => x.CreatedBy)
				.Include(x => x.Instance)
				.ToListAsync(cancellationToken);
			var instances = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
			var apis = instances.Select(x => x.ToApi());
			var moveJobs = await moveJobTasks.ConfigureAwait(false);
			foreach(var I in moveJobs)
				apis.Where(x => x.Id == I.Instance.Id).First().MoveJob = I.ToApi(); // if this .First() fails i will personally murder kevinz000 because I just know he is somehow responsible
			return Json(apis);
		}

		/// <summary>
		/// Get a specific <see cref="Api.Models.Instance"/>.
		/// </summary>
		/// <param name="id">The <see cref="Api.Models.Instance.Id"/> to retrieve.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the request.</returns>
		/// <response code="200">Retrieved <see cref="Api.Models.Instance"/> successfully.</response>
		/// <response code="410">Instance not available.</response>
		[HttpGet("{id}")]
		[TgsAuthorize(InstanceManagerRights.List | InstanceManagerRights.Read)]
		[ProducesResponseType(typeof(Api.Models.Instance), 200)]
		[ProducesResponseType(410)]
		public async Task<IActionResult> GetId(long id, CancellationToken cancellationToken)
		{
			var query = DatabaseContext.Instances.Where(x => x.Id == id);
			var cantList = !AuthenticationContext.User.InstanceManagerRights.Value.HasFlag(InstanceManagerRights.List);

			if (cantList)
				query = query.Include(x => x.InstanceUsers);

			var moveJobTask = query
				.SelectMany(x => x.Jobs)
#pragma warning disable CA1307 // Specify StringComparison
				.Where(x => !x.StoppedAt.HasValue && x.Description.StartsWith(MoveInstanceJobPrefix))
#pragma warning restore CA1307 // Specify StringComparison
				.Include(x => x.StartedBy).ThenInclude(x => x.CreatedBy)
				.FirstOrDefaultAsync(cancellationToken);
			var instance = await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

			if (instance == null)
				return StatusCode((int)HttpStatusCode.Gone);

			if (cantList && !instance.InstanceUsers.Any(x => x.UserId == AuthenticationContext.User.Id && x.AnyRights))
				return Forbid();

			var api = instance.ToApi();
			api.MoveJob = (await moveJobTask.ConfigureAwait(false))?.ToApi();
			return Json(api);
		}
	}
}
