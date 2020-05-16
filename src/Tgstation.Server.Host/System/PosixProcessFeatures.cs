﻿using Microsoft.Extensions.Logging;
using Mono.Unix;
using Mono.Unix.Native;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host.System
{
	/// <inheritdoc />
	sealed class PosixProcessFeatures : IProcessFeatures
	{
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="PosixProcessFeatures"/>.
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="ILogger{TCategoryName}"/> for the <see cref="PosixProcessFeatures"/>.
		/// </summary>
		readonly ILogger<PosixProcessFeatures> logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="PosixProcessFeatures"/> <see langword="class"/>.
		/// </summary>
		/// <param name="ioManager">The value of <see cref="ioManager"/>.</param>
		/// <param name="logger">The value of <see cref="logger"/>.</param>
		public PosixProcessFeatures(IIOManager ioManager, ILogger<PosixProcessFeatures> logger)
		{
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <inheritdoc />
		public void ResumeProcess(global::System.Diagnostics.Process process)
		{
			try
			{
				var result = Syscall.kill(process.Id, Signum.SIGCONT);
				if (result != 0)
					throw new UnixIOException(result);
				logger.LogTrace("Resumed PID {0}", process.Id);
			}
			catch (Exception e)
			{
				logger.LogError(e, "Failed to resume PID {0}!", process.Id);
				throw;
			}
		}

		/// <inheritdoc />
		public void SuspendProcess(global::System.Diagnostics.Process process)
		{
			try
			{
				var result = Syscall.kill(process.Id, Signum.SIGSTOP);
				if (result != 0)
					throw new UnixIOException(result);
				logger.LogTrace("Resumed PID {0}", process.Id);
			}
			catch (Exception e)
			{
				logger.LogError(e, "Failed to suspend PID {0}!", process.Id);
				throw;
			}
		}

		/// <inheritdoc />
		public async Task<string> GetExecutingUsername(global::System.Diagnostics.Process process, CancellationToken cancellationToken)
		{
			if (process == null)
				throw new ArgumentNullException(nameof(process));

			// Need to read /proc/[pid]/status
			// http://man7.org/linux/man-pages/man5/proc.5.html
			// https://unix.stackexchange.com/questions/102676/why-is-uid-information-not-in-proc-x-stat
			var pid = process.Id;
			var statusFile = ioManager.ConcatPath("/proc", pid.ToString(CultureInfo.InvariantCulture), "status");

			global::System.Console.WriteLine(statusFile);
			var statusBytes = await ioManager.ReadAllBytes(statusFile, cancellationToken).ConfigureAwait(false);
			var statusText = Encoding.UTF8.GetString(statusBytes);

			// OH GOD DONT LET ME FORGET THIS
			global::System.Console.WriteLine(statusText);
			var splits = statusText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			var entry = splits.FirstOrDefault(x => x.Trim().StartsWith("Uid:", StringComparison.Ordinal));
			if (entry == default)
				return "UNKNOWN";

			return entry
				.Substring(4)
				.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault(x => !String.IsNullOrWhiteSpace(x))
				?? "UNPARSABLE";
		}
	}
}
