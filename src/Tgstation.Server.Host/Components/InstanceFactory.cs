﻿using Byond.TopicSender;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Components.Byond;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Chat.Commands;
using Tgstation.Server.Host.Components.Deployment;
using Tgstation.Server.Host.Components.Repository;
using Tgstation.Server.Host.Components.Watchdog;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Security;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components
{
	/// <inheritdoc />
	sealed class InstanceFactory : IInstanceFactory
	{
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IDatabaseContextFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IDatabaseContextFactory databaseContextFactory;

		/// <summary>
		/// The <see cref="IApplication"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IApplication application;

		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ILoggerFactory loggerFactory;

		/// <summary>
		/// The <see cref="IByondTopicSender"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IByondTopicSender byondTopicSender;

		/// <summary>
		/// The <see cref="ICryptographySuite"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ICryptographySuite cryptographySuite;

		/// <summary>
		/// The <see cref="ISynchronousIOManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ISynchronousIOManager synchronousIOManager;

		/// <summary>
		/// The <see cref="ISymlinkFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ISymlinkFactory symlinkFactory;

		/// <summary>
		/// The <see cref="IByondInstaller"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IByondInstaller byondInstaller;

		/// <summary>
		/// The <see cref="IProviderFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IChatFactory chatFactory;

		/// <summary>
		/// The <see cref="IProcessExecutor"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IProcessExecutor processExecutor;

		/// <summary>
		/// The <see cref="IPostWriteHandler"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IPostWriteHandler postWriteHandler;

		/// <summary>
		/// The <see cref="IWatchdogFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IWatchdogFactory watchdogFactory;

		/// <summary>
		/// The <see cref="IJobManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IJobManager jobManager;

		/// <summary>
		/// The <see cref="INetworkPromptReaper"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly INetworkPromptReaper networkPromptReaper;

		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// The <see cref="IRepositoryFactory"/> for the <see cref="InstanceFactory"/>.
		/// </summary>
		readonly IRepositoryFactory repositoryFactory;

		/// <summary>
		/// Construct an <see cref="InstanceFactory"/>
		/// </summary>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="databaseContextFactory">The value of <see cref="databaseContextFactory"/></param>
		/// <param name="application">The value of <see cref="application"/></param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/></param>
		/// <param name="byondTopicSender">The value of <see cref="byondTopicSender"/></param>
		/// <param name="cryptographySuite">The value of <see cref="cryptographySuite"/></param>
		/// <param name="synchronousIOManager">The value of <see cref="synchronousIOManager"/></param>
		/// <param name="symlinkFactory">The value of <see cref="symlinkFactory"/></param>
		/// <param name="byondInstaller">The value of <see cref="byondInstaller"/></param>
		/// <param name="chatFactory">The value of <see cref="chatFactory"/></param>
		/// <param name="processExecutor">The value of <see cref="processExecutor"/></param>
		/// <param name="postWriteHandler">The value of <see cref="postWriteHandler"/></param>
		/// <param name="watchdogFactory">The value of <see cref="watchdogFactory"/></param>
		/// <param name="jobManager">The value of <see cref="jobManager"/></param>
		/// <param name="networkPromptReaper">The value of <see cref="networkPromptReaper"/></param>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/></param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/></param>
		/// <param name="repositoryFactory">The value of <see cref="repositoryFactory"/>.</param>
		public InstanceFactory(
			IIOManager ioManager,
			IDatabaseContextFactory databaseContextFactory,
			IApplication application,
			ILoggerFactory loggerFactory,
			IByondTopicSender byondTopicSender,
			ICryptographySuite cryptographySuite,
			ISynchronousIOManager synchronousIOManager,
			ISymlinkFactory symlinkFactory,
			IByondInstaller byondInstaller,
			IChatFactory chatFactory,
			IProcessExecutor processExecutor,
			IPostWriteHandler postWriteHandler,
			IWatchdogFactory watchdogFactory,
			IJobManager jobManager,
			INetworkPromptReaper networkPromptReaper,
			IGitHubClientFactory gitHubClientFactory,
			IPlatformIdentifier platformIdentifier,
			IRepositoryFactory repositoryFactory)
		{
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.databaseContextFactory = databaseContextFactory ?? throw new ArgumentNullException(nameof(databaseContextFactory));
			this.application = application ?? throw new ArgumentNullException(nameof(application));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			this.byondTopicSender = byondTopicSender ?? throw new ArgumentNullException(nameof(byondTopicSender));
			this.cryptographySuite = cryptographySuite ?? throw new ArgumentNullException(nameof(cryptographySuite));
			this.synchronousIOManager = synchronousIOManager ?? throw new ArgumentNullException(nameof(synchronousIOManager));
			this.symlinkFactory = symlinkFactory ?? throw new ArgumentNullException(nameof(symlinkFactory));
			this.byondInstaller = byondInstaller ?? throw new ArgumentNullException(nameof(byondInstaller));
			this.chatFactory = chatFactory ?? throw new ArgumentNullException(nameof(chatFactory));
			this.processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
			this.postWriteHandler = postWriteHandler ?? throw new ArgumentNullException(nameof(postWriteHandler));
			this.watchdogFactory = watchdogFactory ?? throw new ArgumentNullException(nameof(watchdogFactory));
			this.jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
			this.networkPromptReaper = networkPromptReaper ?? throw new ArgumentNullException(nameof(networkPromptReaper));
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
			this.repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
		}

		/// <inheritdoc />
#pragma warning disable CA1506 // TODO: Decomplexify
		public IInstance CreateInstance(Models.Instance metadata)
		{
			// Create the ioManager for the instance
			var instanceIoManager = new ResolvingIOManager(ioManager, metadata.Path);

			// various other ioManagers
			var repoIoManager = new ResolvingIOManager(instanceIoManager, "Repository");
			var byondIOManager = new ResolvingIOManager(instanceIoManager, "Byond");
			var gameIoManager = new ResolvingIOManager(instanceIoManager, "Game");
			var configurationIoManager = new ResolvingIOManager(instanceIoManager, "Configuration");

			var configuration = new StaticFiles.Configuration(configurationIoManager, synchronousIOManager, symlinkFactory, processExecutor, postWriteHandler, platformIdentifier, loggerFactory.CreateLogger<StaticFiles.Configuration>());
			var eventConsumer = new EventConsumer(configuration);
			var repoManager = new RepositoryManager(
				repositoryFactory,
				repoIoManager,
				eventConsumer,
				loggerFactory.CreateLogger<Repository.Repository>(),
				loggerFactory.CreateLogger<RepositoryManager>(),
				metadata.RepositorySettings);
			try
			{
				var byond = new ByondManager(byondIOManager, byondInstaller, eventConsumer, loggerFactory.CreateLogger<ByondManager>());

				var commandFactory = new CommandFactory(application, byond, repoManager, databaseContextFactory, metadata);

				var chat = chatFactory.CreateChat(instanceIoManager, commandFactory, metadata.ChatSettings);
				try
				{
					var sessionControllerFactory = new SessionControllerFactory(processExecutor, byond, byondTopicSender, cryptographySuite, application, gameIoManager, chat, networkPromptReaper, platformIdentifier, loggerFactory, metadata.CloneMetadata());

					var dmbFactory = new DmbFactory(databaseContextFactory, gameIoManager, loggerFactory.CreateLogger<DmbFactory>(), metadata.CloneMetadata());
					try
					{
						var reattachInfoHandler = new ReattachInfoHandler(databaseContextFactory, dmbFactory, loggerFactory.CreateLogger<ReattachInfoHandler>(), metadata.CloneMetadata());
						var watchdog = watchdogFactory.CreateWatchdog(
							chat,
							dmbFactory,
							reattachInfoHandler,
							configuration,
							sessionControllerFactory,
							gameIoManager,
							metadata.CloneMetadata(),
							metadata.DreamDaemonSettings);
						eventConsumer.SetWatchdog(watchdog);
						commandFactory.SetWatchdog(watchdog);
						try
						{
							var dreamMaker = new DreamMaker(byond, gameIoManager, configuration, sessionControllerFactory, eventConsumer, chat, processExecutor, watchdog, loggerFactory.CreateLogger<DreamMaker>());

							return new Instance(metadata.CloneMetadata(), repoManager, byond, dreamMaker, watchdog, chat, configuration, dmbFactory, databaseContextFactory, dmbFactory, jobManager, eventConsumer, gitHubClientFactory, loggerFactory.CreateLogger<Instance>());
						}
						catch
						{
							watchdog.Dispose();
							throw;
						}
					}
					catch
					{
						dmbFactory.Dispose();
						throw;
					}
				}
				catch
				{
					chat.Dispose();
					throw;
				}
			}
			catch
			{
				repoManager.Dispose();
				throw;
			}
		}
#pragma warning restore CA1506

		/// <inheritdoc />
		public Task StartAsync(CancellationToken cancellationToken)
		{
			CheckSystemCompatibility();
			return byondInstaller.CleanCache(cancellationToken);
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		/// <summary>
		/// Test that the <see cref="repositoryFactory"/> is functional.
		/// </summary>
		private void CheckSystemCompatibility() => repositoryFactory.CreateInMemory().Dispose();
	}
}
