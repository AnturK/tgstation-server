﻿using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host
{
	/// <summary>
	/// For creating <see cref="IServer"/>s
	/// </summary>
	public interface IServerFactory
	{
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="IServerFactory"/>.
		/// </summary>
		IIOManager IOManager { get; }

		/// <summary>
		/// Create a <see cref="IServer"/>
		/// </summary>
		/// <param name="args">The arguments for the <see cref="IServer"/></param>
		/// <param name="updatePath">The directory in which to install server updates</param>
		/// <returns>A new <see cref="IServer"/></returns>
		IServer CreateServer(string[] args, string updatePath);
	}
}
