﻿using Byond.TopicSender;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Internal;
using Tgstation.Server.Host.Components.Byond;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Deployment;
using Tgstation.Server.Host.Components.Interop;
using Tgstation.Server.Host.Components.Interop.Bridge;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Security;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components.Session
{
	/// <inheritdoc />
	sealed class SessionControllerFactory : ISessionControllerFactory
	{
		/// <summary>
		/// The <see cref="IProcessExecutor"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IProcessExecutor processExecutor;

		/// <summary>
		/// The <see cref="IByondManager"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IByondManager byond;

		/// <summary>
		/// The <see cref="ITopicClient"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly ITopicClient byondTopicSender;

		/// <summary>
		/// The <see cref="ICryptographySuite"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly ICryptographySuite cryptographySuite;

		/// <summary>
		/// The <see cref="IAssemblyInformationProvider"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IAssemblyInformationProvider assemblyInformationProvider;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IChatManager"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IChatManager chat;

		/// <summary>
		/// The <see cref="INetworkPromptReaper"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly INetworkPromptReaper networkPromptReaper;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// The <see cref="IBridgeRegistrar"/> for the <see cref="SessionControllerFactory"/>.
		/// </summary>
		readonly IBridgeRegistrar bridgeRegistrar;

		/// <summary>
		/// The <see cref="IServerPortProvider"/> for the <see cref="SessionControllerFactory"/>.
		/// </summary>
		readonly IServerPortProvider serverPortProvider;

		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly ILoggerFactory loggerFactory;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly ILogger<SessionControllerFactory> logger;

		/// <summary>
		/// The <see cref="Api.Models.Instance"/> for the <see cref="SessionControllerFactory"/>
		/// </summary>
		readonly Api.Models.Instance instance;

		/// <summary>
		/// Change a given <paramref name="securityLevel"/> into the appropriate DreamDaemon command line word
		/// </summary>
		/// <param name="securityLevel">The <see cref="DreamDaemonSecurity"/> level to change</param>
		/// <returns>A <see cref="string"/> representation of the command line parameter</returns>
		static string SecurityWord(DreamDaemonSecurity securityLevel)
		{
			return securityLevel switch
			{
				DreamDaemonSecurity.Safe => "safe",
				DreamDaemonSecurity.Trusted => "trusted",
				DreamDaemonSecurity.Ultrasafe => "ultrasafe",
				_ => throw new ArgumentOutOfRangeException(nameof(securityLevel), securityLevel, String.Format(CultureInfo.InvariantCulture, "Bad DreamDaemon security level: {0}", securityLevel)),
			};
		}

		/// <summary>
		/// Construct a <see cref="SessionControllerFactory"/>
		/// </summary>
		/// <param name="processExecutor">The value of <see cref="processExecutor"/></param>
		/// <param name="byond">The value of <see cref="byond"/></param>
		/// <param name="byondTopicSender">The value of <see cref="byondTopicSender"/></param>
		/// <param name="cryptographySuite">The value of <see cref="cryptographySuite"/></param>
		/// <param name="assemblyInformationProvider">The value of <see cref="assemblyInformationProvider"/></param>
		/// <param name="instance">The value of <see cref="instance"/></param>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="chat">The value of <see cref="chat"/></param>
		/// <param name="networkPromptReaper">The value of <see cref="networkPromptReaper"/></param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/></param>
		/// <param name="bridgeRegistrar">The value of <see cref="bridgeRegistrar"/>.</param>
		/// <param name="serverPortProvider">The value of <see cref="serverPortProvider"/>.</param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/></param>
		/// <param name="logger">The value of <see cref="logger"/>.</param>
		public SessionControllerFactory(
			IProcessExecutor processExecutor,
			IByondManager byond,
			ITopicClient byondTopicSender,
			ICryptographySuite cryptographySuite,
			IAssemblyInformationProvider assemblyInformationProvider,
			IIOManager ioManager,
			IChatManager chat,
			INetworkPromptReaper networkPromptReaper,
			IPlatformIdentifier platformIdentifier,
			IBridgeRegistrar bridgeRegistrar,
			IServerPortProvider serverPortProvider,
			ILoggerFactory loggerFactory,
			ILogger<SessionControllerFactory> logger,
			Api.Models.Instance instance)
		{
			this.processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
			this.byond = byond ?? throw new ArgumentNullException(nameof(byond));
			this.byondTopicSender = byondTopicSender ?? throw new ArgumentNullException(nameof(byondTopicSender));
			this.cryptographySuite = cryptographySuite ?? throw new ArgumentNullException(nameof(cryptographySuite));
			this.assemblyInformationProvider = assemblyInformationProvider ?? throw new ArgumentNullException(nameof(assemblyInformationProvider));
			this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.chat = chat ?? throw new ArgumentNullException(nameof(chat));
			this.networkPromptReaper = networkPromptReaper ?? throw new ArgumentNullException(nameof(networkPromptReaper));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
			this.bridgeRegistrar = bridgeRegistrar ?? throw new ArgumentNullException(nameof(bridgeRegistrar));
			this.serverPortProvider = serverPortProvider ?? throw new ArgumentNullException(nameof(serverPortProvider));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <inheritdoc />
		#pragma warning disable CA1506 // TODO: Decomplexify
		public async Task<ISessionController> LaunchNew(
			IDmbProvider dmbProvider,
			IByondExecutableLock currentByondLock,
			DreamDaemonLaunchParameters launchParameters,
			bool primaryPort,
			bool primaryDirectory,
			bool apiValidate,
			CancellationToken cancellationToken)
		{
			var portToUse = primaryPort ? launchParameters.PrimaryPort : launchParameters.SecondaryPort;
			if (!portToUse.HasValue)
				throw new InvalidOperationException("Given port is null!");

			var basePath = primaryDirectory ? dmbProvider.PrimaryDirectory : dmbProvider.SecondaryDirectory;

			switch (dmbProvider.CompileJob.MinimumSecurityLevel)
			{
				case DreamDaemonSecurity.Ultrasafe:
					break;
				case DreamDaemonSecurity.Safe:
					if (launchParameters.SecurityLevel == DreamDaemonSecurity.Ultrasafe)
						launchParameters.SecurityLevel = DreamDaemonSecurity.Safe;
					break;
				case DreamDaemonSecurity.Trusted:
					launchParameters.SecurityLevel = DreamDaemonSecurity.Trusted;
					break;
				default:
					throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Invalid DreamDaemonSecurity value: {0}", dmbProvider.CompileJob.MinimumSecurityLevel));
			}

			var chatTrackingContext = chat.CreateTrackingContext();
			try
			{
				// get the byond lock
				var byondLock = currentByondLock ?? await byond.UseExecutables(Version.Parse(dmbProvider.CompileJob.ByondVersion), cancellationToken).ConfigureAwait(false);
				try
				{
					if (launchParameters.SecurityLevel == DreamDaemonSecurity.Trusted)
						await byondLock.TrustDmbPath(ioManager.ConcatPath(basePath, dmbProvider.DmbName), cancellationToken).ConfigureAwait(false);

					PortBindTest(portToUse.Value);
					await CheckPagerIsNotRunning(cancellationToken).ConfigureAwait(false);

					var accessIdentifier = cryptographySuite.GetSecureString();

					// set command line options
					// more sanitization here cause it uses the same scheme
					var parameters = $"{DMApiConstants.ParamApiVersion}={byondTopicSender.SanitizeString(DMApiConstants.Version.Semver().ToString())}&{byondTopicSender.SanitizeString(DMApiConstants.ParamServerPort)}={serverPortProvider.HttpApiPort}&{byondTopicSender.SanitizeString(DMApiConstants.ParamAccessIdentifier)}={byondTopicSender.SanitizeString(accessIdentifier)}";

					var visibility = apiValidate ? "invisible" : "public";

					// important to run on all ports to allow port changing
					var arguments = String.Format(CultureInfo.InvariantCulture, "{0} -port {1} -ports 1-65535 {2}-close -{3} -{4} -public -params \"{5}\"",
						dmbProvider.DmbName,
						portToUse,
						launchParameters.AllowWebClient.Value ? "-webclient " : String.Empty,
						SecurityWord(launchParameters.SecurityLevel.Value),
						visibility,
						parameters);

					// See https://github.com/tgstation/tgstation-server/issues/719
					var noShellExecute = !platformIdentifier.IsWindows;

					// launch dd
					var process = processExecutor.LaunchProcess(
						byondLock.DreamDaemonPath,
						basePath,
						arguments,
						noShellExecute,
						noShellExecute,
						noShellExecute: noShellExecute);

					if (noShellExecute)
					{
						// Log DD output
						_ = process.Lifetime.ContinueWith(
								x => logger.LogTrace(
									"DreamDaemon Output:{0}{1}",
									Environment.NewLine, process.GetCombinedOutput()),
								TaskScheduler.Current);
					}

					try
					{
						networkPromptReaper.RegisterProcess(process);

						var runtimeInformation = CreateRuntimeInformation(
							dmbProvider,
							chatTrackingContext,
							launchParameters.SecurityLevel.Value,
							apiValidate);

						var reattachInformation = new ReattachInformation(
							dmbProvider,
							process,
							runtimeInformation,
							accessIdentifier,
							portToUse.Value,
							primaryDirectory);

						var sessionController = new SessionController(
							reattachInformation,
							process,
							byondLock,
							byondTopicSender,
							chatTrackingContext,
							bridgeRegistrar,
							chat,
							loggerFactory.CreateLogger<SessionController>(),
							launchParameters.StartupTimeout,
							false);

						return sessionController;
					}
					catch
					{
						process.Terminate();
						process.Dispose();
						throw;
					}
				}
				catch
				{
					if (currentByondLock == null)
						byondLock.Dispose();
					throw;
				}
			}
			catch
			{
				chatTrackingContext.Dispose();
				throw;
			}
		}
		#pragma warning restore CA1506

		/// <inheritdoc />
		public async Task<ISessionController> Reattach(
			ReattachInformation reattachInformation,
			CancellationToken cancellationToken)
		{
			if (reattachInformation == null)
				throw new ArgumentNullException(nameof(reattachInformation));

			var chatTrackingContext = chat.CreateTrackingContext();
			try
			{
				var byondLock = await byond.UseExecutables(Version.Parse(reattachInformation.Dmb.CompileJob.ByondVersion), cancellationToken).ConfigureAwait(false);
				try
				{
					var process = processExecutor.GetProcess(reattachInformation.ProcessId);
					if (process == null)
						return null;

					try
					{
						networkPromptReaper.RegisterProcess(process);
						var runtimeInformation = CreateRuntimeInformation(
							reattachInformation.Dmb,
							chatTrackingContext,
							null,
							false);
						reattachInformation.SetRuntimeInformation(runtimeInformation);

						var controller = new SessionController(
							reattachInformation,
							process,
							byondLock,
							byondTopicSender,
							chatTrackingContext,
							bridgeRegistrar,
							chat,
							loggerFactory.CreateLogger<SessionController>(),
							null,
							true);

						process = null;
						byondLock = null;
						chatTrackingContext = null;

						return controller;
					}
					finally
					{
						process?.Dispose();
					}
				}
				finally
				{
					byondLock?.Dispose();
				}
			}
			finally
			{
				chatTrackingContext?.Dispose();
			}
		}

		/// <inheritdoc />
		public ISessionController CreateDeadSession(IDmbProvider dmbProvider) => new DeadSessionController(dmbProvider);

		/// <summary>
		/// Create <see cref="RuntimeInformation"/>.
		/// </summary>
		/// <param name="dmbProvider">The <see cref="IDmbProvider"/>.</param>
		/// <param name="chatTrackingContext">The <see cref="IChatTrackingContext"/>.</param>
		/// <param name="securityLevel">The <see cref="DreamDaemonSecurity"/> level if any.</param>
		/// <param name="apiValidateOnly">The value of <see cref="RuntimeInformation.ApiValidateOnly"/>.</param>
		/// <returns>A new <see cref="RuntimeInformation"/> <see langword="class"/>.</returns>
		RuntimeInformation CreateRuntimeInformation(
			IDmbProvider dmbProvider,
			IChatTrackingContext chatTrackingContext,
			DreamDaemonSecurity? securityLevel,
			bool apiValidateOnly)
		{
			var revisionInfo = new Api.Models.Internal.RevisionInformation
			{
				CommitSha = dmbProvider.CompileJob.RevisionInformation.CommitSha,
				OriginCommitSha = dmbProvider.CompileJob.RevisionInformation.OriginCommitSha
			};

			var testMerges = dmbProvider
				.CompileJob
				.RevisionInformation
				.ActiveTestMerges?
				.Select(x => x.TestMerge)
				.Select(x => new TestMergeInformation(x, revisionInfo))
				?? Enumerable.Empty<TestMergeInformation>();

			return new RuntimeInformation(
				assemblyInformationProvider,
				serverPortProvider,
				testMerges,
				chatTrackingContext.Channels,
				instance,
				revisionInfo,
				securityLevel,
				apiValidateOnly);
		}

		/// <summary>
		/// Make sure the BYOND pager is not running.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task"/> representing the running operation.</returns>
		async Task CheckPagerIsNotRunning(CancellationToken cancellationToken)
		{
			if (!platformIdentifier.IsWindows)
				return;

			using var otherProcess = processExecutor.GetProcessByName("byond");
			if (otherProcess == null)
				return;

			var otherUsernameTask = otherProcess.GetExecutingUsername(cancellationToken);
			using var ourProcess = processExecutor.GetCurrentProcess();
			var ourUserName = await ourProcess.GetExecutingUsername(cancellationToken).ConfigureAwait(false);
			var otherUserName = await otherUsernameTask.ConfigureAwait(false);

			if(otherUserName.Equals(ourUserName, StringComparison.Ordinal))
				throw new JobException(ErrorCode.DeploymentPagerRunning);
		}

		/// <summary>
		/// Check if a given <paramref name="port"/> can be bound to.
		/// </summary>
		/// <param name="port">The port number to test.</param>
		static void PortBindTest(ushort port)
		{
			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			try
			{
				socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
			}
			catch (Exception ex)
			{
				throw new JobException(ErrorCode.DreamDaemonPortInUse, ex);
			}
		}
	}
}
