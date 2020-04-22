﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Components.Chat.Commands;

namespace Tgstation.Server.Host.Components.Chat
{
	/// <summary>
	/// Represents a tracking of dynamic chat json files
	/// </summary>
	public interface IJsonTrackingContext : IDisposable
	{
		/// <summary>
		/// If the <see cref="IJsonTrackingContext"/> should be used for <see cref="GetCustomCommands(CancellationToken)"/>
		/// </summary>
		bool Active { get; set; }

		/// <summary>
		/// Read <see cref="CustomCommand"/>s from the <see cref="IJsonTrackingContext"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="IReadOnlyList{T}"/> of <see cref="CustomCommand"/>s in the <see cref="IJsonTrackingContext"/></returns>
		Task<IReadOnlyList<CustomCommand>> GetCustomCommands(CancellationToken cancellationToken);

		/// <summary>
		/// Writes information about connected <paramref name="channels"/> to the <see cref="IJsonTrackingContext"/>
		/// </summary>
		/// <param name="channels">The <see cref="ChatChannel"/>s to write out</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task SetChannels(IEnumerable<ChatChannel> channels, CancellationToken cancellationToken);
	}
}