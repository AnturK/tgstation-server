﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tgstation.Server.Host.Database
{
	/// <summary>
	/// Represents a database table.
	/// </summary>
	/// <typeparam name="TModel">The type of model.</typeparam>
	public interface IDatabaseCollection<TModel> : IQueryable<TModel>
	{
		/// <summary>
		/// An <see cref="IEnumerable{T}"/> of <typeparamref name="TModel"/>s prioritizing in the working set.
		/// </summary>
		IEnumerable<TModel> Local { get; }

		/// <summary>
		/// Add a given <paramref name="model"/> to the the working set.
		/// </summary>
		/// <param name="model">The <typeparamref name="TModel"/> model to add.</param>
		void Add(TModel model);

		/// <summary>
		/// Remove a given <paramref name="model"/> from the the working set.
		/// </summary>
		/// <param name="model">The <typeparamref name="TModel"/> model to remove.</param>
		void Remove(TModel model);

		/// <summary>
		/// Attach a given <paramref name="model"/> to the the working set.
		/// </summary>
		/// <param name="model">The <typeparamref name="TModel"/> model to add.</param>
		void Attach(TModel model);

		/// <summary>
		/// Add a range of <paramref name="models"/> to the <see cref="IDatabaseCollection{TModel}"/>.
		/// </summary>
		/// <param name="models">An <see cref="IEnumerable{T}"/> of <typeparamref name="TModel"/>s to add.</param>
		void AddRange(IEnumerable<TModel> models);

		/// <summary>
		/// Remove a range of <paramref name="models"/> from the <see cref="IDatabaseCollection{TModel}"/>.
		/// </summary>
		/// <param name="models">An <see cref="IEnumerable{T}"/> of <typeparamref name="TModel"/>s to remove.</param>
		void RemoveRange(IEnumerable<TModel> models);

		/// <summary>
		/// Asyncronously run a given <paramref name="action"/> on the <see cref="IDatabaseCollection{TModel}"/>.
		/// </summary>
		/// <param name="action">The <see cref="Action{T}"/> to run.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task"/> representing the running operation.</returns>
		Task ForEachAsync(Action<TModel> action, CancellationToken cancellationToken);

		/// <summary>
		/// Retrieve all the <typeparamref name="TModel"/>s in the table.
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="List{T}"/> of all <typeparamref name="TModel"/> in the table.</returns>
		Task<List<TModel>> ToListAsync(CancellationToken cancellationToken);
	}
}
