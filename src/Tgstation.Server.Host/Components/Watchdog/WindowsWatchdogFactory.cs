﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using Tgstation.Server.Api.Models.Internal;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Deployment;
using Tgstation.Server.Host.Components.Session;
using Tgstation.Server.Host.Configuration;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components.Watchdog
{
	/// <summary>
	/// <see cref="IWatchdogFactory"/> for creating <see cref="WindowsWatchdog"/>s.
	/// </summary>
	sealed class WindowsWatchdogFactory : WatchdogFactory
	{
		/// <summary>
		/// The <see cref="ISymlinkFactory"/> for the <see cref="WindowsWatchdogFactory"/>.
		/// </summary>
		readonly ISymlinkFactory symlinkFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="WindowsWatchdogFactory"/> <see langword="class"/>.
		/// </summary>
		/// <param name="serverControl">The <see cref="IServerControl"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="loggerFactory">The <see cref="ILoggerFactory"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="databaseContextFactory">The <see cref="IDatabaseContextFactory"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="jobManager">The <see cref="IJobManager"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="asyncDelayer">The <see cref="IAsyncDelayer"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="assemblyInformationProvider">The <see cref="IAssemblyInformationProvider"/> for the <see cref="WatchdogFactory"/>.</param>
		/// <param name="symlinkFactory">The value of <see cref="symlinkFactory"/>.</param>
		/// <param name="generalConfigurationOptions">The <see cref="IOptions{TOptions}"/> for <see cref="GeneralConfiguration"/> for the <see cref="WatchdogFactory"/>.</param>
		public WindowsWatchdogFactory(
			IServerControl serverControl,
			ILoggerFactory loggerFactory,
			IDatabaseContextFactory databaseContextFactory,
			IJobManager jobManager,
			IAsyncDelayer asyncDelayer,
			IAssemblyInformationProvider assemblyInformationProvider,
			ISymlinkFactory symlinkFactory,
			IOptions<GeneralConfiguration> generalConfigurationOptions)
			: base(
				serverControl,
				loggerFactory,
				databaseContextFactory,
				jobManager,
				asyncDelayer,
				assemblyInformationProvider,
				generalConfigurationOptions)
		{
			this.symlinkFactory = symlinkFactory ?? throw new ArgumentNullException(nameof(symlinkFactory));
		}

		/// <inheritdoc />
		protected override IWatchdog CreateNonExperimentalWatchdog(
			IChatManager chat,
			IDmbFactory dmbFactory,
			IReattachInfoHandler reattachInfoHandler,
			ISessionControllerFactory sessionControllerFactory,
			IIOManager ioManager,
			Api.Models.Instance instance,
			DreamDaemonSettings settings)
			=> new WindowsWatchdog(
				chat,
				sessionControllerFactory,
				dmbFactory,
				reattachInfoHandler,
				DatabaseContextFactory,
				JobManager,
				ServerControl,
				AsyncDelayer,
				AssemblyInformationProvider,
				ioManager,
				symlinkFactory,
				LoggerFactory.CreateLogger<WindowsWatchdog>(),
				settings,
				instance,
				settings.AutoStart.Value);
	}
}
