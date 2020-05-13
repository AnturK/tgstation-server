﻿using System;
using System.ComponentModel.DataAnnotations;

namespace Tgstation.Server.Api.Models.Internal
{
	/// <summary>
	/// Launch settings for DreamDaemon
	/// </summary>
	public class DreamDaemonLaunchParameters
	{
		/// <summary>
		/// If the BYOND web client can be used to connect to the game server
		/// </summary>
		[Required]
		public bool? AllowWebClient { get; set; }

		/// <summary>
		/// The <see cref="DreamDaemonSecurity"/> level of <see cref="DreamDaemon"/>
		/// </summary>
		[Required]
		[EnumDataType(typeof(DreamDaemonSecurity))]
		public DreamDaemonSecurity? SecurityLevel { get; set; }

		/// <summary>
		/// The first port <see cref="DreamDaemon"/> uses. This should be the publically advertised port
		/// </summary>
		[Required]
		[Range(1, 65535)]
		public ushort? PrimaryPort { get; set; }

		/// <summary>
		/// The second port <see cref="DreamDaemon"/> uses
		/// </summary>
		[Required]
		[Range(1, 65535)]
		public ushort? SecondaryPort { get; set; }

		/// <summary>
		/// The DreamDaemon startup timeout in seconds
		/// </summary>
		[Required]
		public uint? StartupTimeout { get; set; }

		/// <summary>
		/// The number of seconds between each watchdog heartbeat. 0 disables.
		/// </summary>
		[Required]
		public uint? HeartbeatSeconds { get; set; }

		/// <summary>
		/// Check if we match a given set of <paramref name="otherParameters"/>
		/// </summary>
		/// <param name="otherParameters">The <see cref="DreamDaemonLaunchParameters"/> to compare against</param>
		/// <returns><see langword="true"/> if they match, <see langword="false"/> otherwise</returns>
		public bool Match(DreamDaemonLaunchParameters otherParameters) =>
			AllowWebClient == (otherParameters?.AllowWebClient ?? throw new ArgumentNullException(nameof(otherParameters)))
				&& SecurityLevel == otherParameters.SecurityLevel
				&& PrimaryPort == otherParameters.PrimaryPort
				&& SecondaryPort == otherParameters.SecondaryPort
				&& StartupTimeout == otherParameters.StartupTimeout
				&& HeartbeatSeconds == otherParameters.HeartbeatSeconds;
	}
}