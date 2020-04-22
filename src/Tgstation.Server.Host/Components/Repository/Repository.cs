﻿using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;

namespace Tgstation.Server.Host.Components.Repository
{
	/// <inheritdoc />
	sealed class Repository : IRepository
	{
		/// <summary>
		/// Indication of a GitHub repository
		/// </summary>
		public const string GitHubUrl = "://github.com/";

		/// <summary>
		/// Template error message for when tracking of the most recent origin commit fails
		/// </summary>
		public const string OriginTrackingErrorTemplate = "Unable to determine most recent origin commit of {0}. Marking it as an origin commit. This may result in invalid git metadata until the next hard reset to an origin reference.";

		/// <summary>
		/// The branch name used for publishing testmerge commits
		/// </summary>
		public const string RemoteTemporaryBranchName = "___TGSTempBranch";

		const string UnknownReference = "<UNKNOWN>";

		/// <inheritdoc />
		public bool IsGitHubRepository { get; }

		/// <inheritdoc />
		public string GitHubOwner { get; }

		/// <inheritdoc />
		public string GitHubRepoName { get; }

		/// <inheritdoc />
		public bool Tracking => Reference != null && repository.Head.IsTracking;

		/// <inheritdoc />
		public string Head => repository.Head.Tip.Sha;

		/// <inheritdoc />
		public string Reference => repository.Head.FriendlyName;

		/// <inheritdoc />
		public string Origin => repository.Network.Remotes.First().Url;

		/// <summary>
		/// The <see cref="LibGit2Sharp.IRepository"/> for the <see cref="Repository"/>
		/// </summary>
		readonly LibGit2Sharp.IRepository repository;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="Repository"/>
		/// </summary>
		readonly IIOManager ioMananger;

		/// <summary>
		/// The <see cref="IEventConsumer"/> for the <see cref="Repository"/>
		/// </summary>
		readonly IEventConsumer eventConsumer;

		/// <summary>
		/// The <see cref="ICredentialsProvider"/> for the <see cref="Repository"/>
		/// </summary>
		readonly ICredentialsProvider credentialsProvider;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="Repository"/>
		/// </summary>
		readonly ILogger<Repository> logger;

		/// <summary>
		/// <see cref="Action"/> to be taken when <see cref="Dispose"/> is called
		/// </summary>
		readonly Action onDispose;

		/// <summary>
		/// If the <see cref="Repository"/> was disposed.
		/// </summary>
		bool disposed;

		/// <summary>
		/// Converts a given <paramref name="progressReporter"/> to a <see cref="LibGit2Sharp.Handlers.CheckoutProgressHandler"/>
		/// </summary>
		/// <param name="progressReporter"><see cref="Action{T1}"/> to report 0-100 <see cref="int"/> progress of the operation</param>
		/// <returns>A <see cref="LibGit2Sharp.Handlers.CheckoutProgressHandler"/> based on <paramref name="progressReporter"/></returns>
		static CheckoutProgressHandler CheckoutProgressHandler(Action<int> progressReporter) => (a, completedSteps, totalSteps) => progressReporter((int)(((float)completedSteps) / totalSteps * 100));

		/// <summary>
		/// Construct a <see cref="Repository"/>
		/// </summary>
		/// <param name="repository">The value of <see cref="repository"/></param>
		/// <param name="ioMananger">The value of <see cref="ioMananger"/></param>
		/// <param name="eventConsumer">The value of <see cref="eventConsumer"/></param>
		/// <param name="credentialsProvider">The value of <see cref="credentialsProvider"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="onDispose">The value if <see cref="onDispose"/></param>
		public Repository(LibGit2Sharp.IRepository repository, IIOManager ioMananger, IEventConsumer eventConsumer, ICredentialsProvider credentialsProvider, ILogger<Repository> logger, Action onDispose)
		{
			this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
			this.ioMananger = ioMananger ?? throw new ArgumentNullException(nameof(ioMananger));
			this.eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
			this.credentialsProvider = credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
			IsGitHubRepository = Origin.Contains(GitHubUrl, StringComparison.InvariantCultureIgnoreCase);
			if (IsGitHubRepository)
			{
				GetRepositoryOwnerName(Origin, out var owner, out var name);
				GitHubOwner = owner;
				GitHubRepoName = name;
			}
		}

		/// <inheritdoc />
		public void Dispose()
		{
			lock (onDispose)
			{
				if (disposed)
					return;

				disposed = true;
			}

			logger.LogTrace("Disposing...");
			repository.Dispose();
			onDispose();
		}

		void GetRepositoryOwnerName(string remote, out string owner, out string name)
		{
			// Assume standard gh format: [(git)|(https)]://github.com/owner/repo(.git)[0-1]
			// Yes use .git twice in case it was weird
			var toRemove = new string[] { ".git", "/", ".git" };
			foreach (string item in toRemove)
				if (remote.EndsWith(item, StringComparison.OrdinalIgnoreCase))
					remote = remote.Substring(0, remote.LastIndexOf(item, StringComparison.OrdinalIgnoreCase));
			var splits = remote.Split('/');
			name = splits[splits.Length - 1];
			owner = splits[splits.Length - 2].Split('.')[0];

			logger.LogTrace("GetRepositoryOwnerName({0}) => {1} / {2}", remote, owner, name);
		}

		/// <summary>
		/// Generate a standard set of <see cref="PushOptions"/>
		/// </summary>
		/// <param name="progressReporter"><see cref="Action{T1}"/> to report 0-100 <see cref="int"/> progress of the operation</param>
		/// <param name="username">The username for the <see cref="credentialsProvider"/></param>
		/// <param name="password">The password for the <see cref="credentialsProvider"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A new set of <see cref="PushOptions"/></returns>
		PushOptions GeneratePushOptions(Action<int> progressReporter, string username, string password, CancellationToken cancellationToken) => new PushOptions
		{
			OnPackBuilderProgress = (stage, current, total) =>
			{
				var baseProgress = stage == PackBuilderStage.Counting ? 0 : 25;
				progressReporter(baseProgress + ((int)(25 * ((float)current) / total)));
				return !cancellationToken.IsCancellationRequested;
			},
			OnNegotiationCompletedBeforePush = (a) => !cancellationToken.IsCancellationRequested,
			OnPushTransferProgress = (a, sentBytes, totalBytes) =>
			{
				progressReporter(50 + ((int)(50 * ((float)sentBytes) / totalBytes)));
				return !cancellationToken.IsCancellationRequested;
			},
			CredentialsProvider = credentialsProvider.GenerateCredentialsHandler(username, password)
		};

		/// <summary>
		/// Runs a blocking force checkout to <paramref name="committish"/>
		/// </summary>
		/// <param name="committish">The committish to checkout</param>
		/// <param name="progressReporter">Progress reporter <see cref="Action{T}"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		void RawCheckout(string committish, Action<int> progressReporter, CancellationToken cancellationToken)
		{
			logger.LogTrace("Checkout: {0}", committish);

			progressReporter(0);
			cancellationToken.ThrowIfCancellationRequested();

			Commands.Checkout(repository, committish, new CheckoutOptions
			{
				CheckoutModifiers = CheckoutModifiers.Force,
				OnCheckoutProgress = CheckoutProgressHandler(progressReporter)
			});

			cancellationToken.ThrowIfCancellationRequested();

			repository.RemoveUntrackedFiles();
		}

		/// <inheritdoc />
		#pragma warning disable CA1506 // TODO: Decomplexify
		public async Task<bool?> AddTestMerge(TestMergeParameters testMergeParameters, string committerName, string committerEmail, string username, string password, Action<int> progressReporter, CancellationToken cancellationToken)
		{
			if (testMergeParameters == null)
				throw new ArgumentNullException(nameof(testMergeParameters));
			if (committerName == null)
				throw new ArgumentNullException(nameof(committerName));
			if (committerEmail == null)
				throw new ArgumentNullException(nameof(committerEmail));
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));

			logger.LogDebug("Begin AddTestMerge: #{0} at {1} ({4}) by <{2} ({3})>", testMergeParameters.Number, testMergeParameters.PullRequestRevision?.Substring(0, 7), committerName, committerEmail, testMergeParameters.Comment);

			if (!IsGitHubRepository)
				throw new InvalidOperationException("Test merging is only available on GitHub hosted origin repositories!");

			var commitMessage = String.Format(
				CultureInfo.InvariantCulture,
				"Test merge of pull request #{0}{1}{2}",
				testMergeParameters.Number,
				testMergeParameters.Comment != null
					? Environment.NewLine
					: String.Empty,
				testMergeParameters.Comment ?? String.Empty);

			var prBranchName = String.Format(CultureInfo.InvariantCulture, "pr-{0}", testMergeParameters.Number);
			var localBranchName = String.Format(CultureInfo.InvariantCulture, "pull/{0}/headrefs/heads/{1}", testMergeParameters.Number, prBranchName);

			var refSpec = String.Format(CultureInfo.InvariantCulture, "pull/{0}/head:{1}", testMergeParameters.Number, prBranchName);
			var refSpecList = new List<string> { refSpec };
			var logMessage = String.Format(CultureInfo.InvariantCulture, "Merge remote pull request #{0}", testMergeParameters.Number);

			var originalCommit = repository.Head;

			MergeResult result = null;

			var sig = new Signature(new Identity(committerName, committerEmail), DateTimeOffset.Now);
			await Task.Factory.StartNew(() =>
			{
				try
				{
					try
					{
						logger.LogTrace("Fetching refspec {0}...", refSpec);

						var remote = repository.Network.Remotes.First();
						progressReporter(0);
						Commands.Fetch((LibGit2Sharp.Repository)repository, remote.Name, refSpecList, new FetchOptions
						{
							Prune = true,
							OnProgress = (a) => !cancellationToken.IsCancellationRequested,
							OnTransferProgress = (a) =>
							{
								var percentage = 50 * (((float)a.IndexedObjects + a.ReceivedObjects) / (a.TotalObjects * 2));
								progressReporter((int)percentage);
								return !cancellationToken.IsCancellationRequested;
							},
							OnUpdateTips = (a, b, c) => !cancellationToken.IsCancellationRequested,
							CredentialsProvider = credentialsProvider.GenerateCredentialsHandler(username, password)
						}, logMessage);
					}
					catch (UserCancelledException) { }

					cancellationToken.ThrowIfCancellationRequested();

					repository.RemoveUntrackedFiles();

					cancellationToken.ThrowIfCancellationRequested();

					testMergeParameters.PullRequestRevision = repository.Lookup(testMergeParameters.PullRequestRevision ?? localBranchName).Sha;

					cancellationToken.ThrowIfCancellationRequested();

					logger.LogTrace("Merging {0} into {1}...", testMergeParameters.PullRequestRevision.Substring(0, 7), Reference);

					result = repository.Merge(testMergeParameters.PullRequestRevision, sig, new MergeOptions
					{
						CommitOnSuccess = commitMessage == null,
						FailOnConflict = true,
						FastForwardStrategy = FastForwardStrategy.NoFastForward,
						SkipReuc = true,
						OnCheckoutProgress = (a, completedSteps, totalSteps) => progressReporter(50 + ((int)(((float)completedSteps) / totalSteps * 50)))
					});
				}
				finally
				{
					repository.Branches.Remove(localBranchName);
				}

				cancellationToken.ThrowIfCancellationRequested();

				if (result.Status == MergeStatus.Conflicts)
				{
					var revertTo = originalCommit.CanonicalName ?? originalCommit.Tip.Sha;
					logger.LogDebug("Merge conflict, aborting and reverting to {0}", revertTo);
					RawCheckout(revertTo, progressReporter, cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
				}

				repository.RemoveUntrackedFiles();
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);

			if (result.Status == MergeStatus.Conflicts)
			{
				await eventConsumer.HandleEvent(EventType.RepoMergeConflict, new List<string> { originalCommit.Tip.Sha, testMergeParameters.PullRequestRevision, originalCommit.FriendlyName ?? UnknownReference, prBranchName }, cancellationToken).ConfigureAwait(false);
				return null;
			}

			if (commitMessage != null && result.Status != MergeStatus.UpToDate)
			{
				logger.LogTrace("Committing merge: \"{0}\"...", commitMessage);
				await Task.Factory.StartNew(() => repository.Commit(commitMessage, sig, sig, new CommitOptions
				{
					PrettifyMessage = true
				}), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
			}

			await eventConsumer.HandleEvent(
				EventType.RepoMergePullRequest,
				new List<string>
				{
					testMergeParameters.Number.ToString(CultureInfo.InvariantCulture),
					testMergeParameters.PullRequestRevision,
					testMergeParameters.Comment
				},
				cancellationToken)
				.ConfigureAwait(false);

			return result.Status != MergeStatus.NonFastForward;
		}
		#pragma warning restore CA1506

		/// <inheritdoc />
		public async Task CheckoutObject(string committish, Action<int> progressReporter, CancellationToken cancellationToken)
		{
			if (committish == null)
				throw new ArgumentNullException(nameof(committish));
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));
			logger.LogDebug("Checkout object: {0}...", committish);
			await eventConsumer.HandleEvent(EventType.RepoCheckout, new List<string> { committish }, cancellationToken).ConfigureAwait(false);
			await Task.Factory.StartNew(() =>
			{
				repository.RemoveUntrackedFiles();
				RawCheckout(committish, progressReporter, cancellationToken);
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task FetchOrigin(string username, string password, Action<int> progressReporter, CancellationToken cancellationToken)
		{
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));
			logger.LogDebug("Fetch origin...");
			await eventConsumer.HandleEvent(EventType.RepoFetch, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
			await Task.Factory.StartNew(() =>
			{
				var remote = repository.Network.Remotes.First();
				try
				{
					Commands.Fetch((LibGit2Sharp.Repository)repository, remote.Name, remote.FetchRefSpecs.Select(x => x.Specification), new FetchOptions
					{
						Prune = true,
						OnProgress = (a) => !cancellationToken.IsCancellationRequested,
						OnTransferProgress = (a) =>
						{
							var percentage = 100 * (((float)a.IndexedObjects + a.ReceivedObjects) / (a.TotalObjects * 2));
							progressReporter((int)percentage);
							return !cancellationToken.IsCancellationRequested;
						},
						OnUpdateTips = (a, b, c) => !cancellationToken.IsCancellationRequested,
						CredentialsProvider = credentialsProvider.GenerateCredentialsHandler(username, password)
					}, "Fetch origin commits");
				}
				catch (UserCancelledException)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}

		/// <summary>
		/// Force push the current repository HEAD to <see cref="RemoteTemporaryBranchName"/>;
		/// </summary>
		/// <param name="username">The username to fetch from the origin repository</param>
		/// <param name="password">The password to fetch from the origin repository</param>
		/// <param name="progressReporter"><see cref="Action{T1}"/> to report 0-100 <see cref="int"/> progress of the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task PushHeadToTemporaryBranch(string username, string password, Action<int> progressReporter, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			logger.LogInformation("Pushing changes to temporary remote branch...");
			var branch = repository.CreateBranch(RemoteTemporaryBranchName);
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var remote = repository.Network.Remotes.First();
				try
				{
					var forcePushString = String.Format(CultureInfo.InvariantCulture, "+{0}:{0}", branch.CanonicalName);
					repository.Network.Push(remote, forcePushString, GeneratePushOptions(progress => progressReporter((int)(0.9f * progress)), username, password, cancellationToken));
					var removalString = String.Format(CultureInfo.InvariantCulture, ":{0}", branch.CanonicalName);
					repository.Network.Push(remote, removalString, GeneratePushOptions(progress => progressReporter(90 + (int)(0.1f * progress)), username, password, cancellationToken));
				}
				catch (UserCancelledException)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}
				catch(LibGit2SharpException e)
				{
					logger.LogWarning("Unable to push to temporary branch! Exception: {0}", e);
				}
			}
			finally
			{
				repository.Branches.Remove(branch);
			}
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public async Task ResetToOrigin(Action<int> progressReporter, CancellationToken cancellationToken)
		{
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));
			if (!Tracking)
				throw new JobException("Cannot reset to origin while not on a tracked reference!");
			logger.LogTrace("Reset to origin...");
			var trackedBranch = repository.Head.TrackedBranch;
			await eventConsumer.HandleEvent(EventType.RepoResetOrigin, new List<string> { trackedBranch.FriendlyName, trackedBranch.Tip.Sha }, cancellationToken).ConfigureAwait(false);
			await ResetToSha(trackedBranch.Tip.Sha, progressReporter, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public Task ResetToSha(string sha, Action<int> progressReporter, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			if (sha == null)
				throw new ArgumentNullException(nameof(sha));
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));

			logger.LogDebug("Reset to sha: {0}", sha.Substring(0, 7));

			repository.RemoveUntrackedFiles();
			cancellationToken.ThrowIfCancellationRequested();

			var gitObject = repository.Lookup(sha, ObjectType.Commit);
			cancellationToken.ThrowIfCancellationRequested();

			if (gitObject == null)
				throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture, "Cannot reset to non-existent SHA: {0}", sha));

			repository.Reset(ResetMode.Hard, gitObject.Peel<Commit>(), new CheckoutOptions
			{
				OnCheckoutProgress = CheckoutProgressHandler(progressReporter)
			});
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public async Task CopyTo(string path, CancellationToken cancellationToken)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			logger.LogTrace("Copying to {0}...", path);
			await ioMananger.CopyDirectory(ioMananger.ResolvePath(), path, new List<string> { ".git" }, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<bool?> MergeOrigin(string committerName, string committerEmail, Action<int> progressReporter, CancellationToken cancellationToken)
		{
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));

			MergeResult result = null;
			Branch trackedBranch = null;

			var oldHead = repository.Head;
			var oldTip = oldHead.Tip;

			await Task.Factory.StartNew(() =>
			{
				if (!Tracking)
					throw new JobException("Cannot reset to origin while not on a tracked reference!");

				repository.RemoveUntrackedFiles();

				cancellationToken.ThrowIfCancellationRequested();

				trackedBranch = repository.Head.TrackedBranch;
				logger.LogDebug("Merge origin/{2}: <{0} ({1})>", committerName, committerEmail, trackedBranch.FriendlyName);
				result = repository.Merge(trackedBranch, new Signature(new Identity(committerName, committerEmail), DateTimeOffset.Now), new MergeOptions
				{
					CommitOnSuccess = true,
					FailOnConflict = true,
					FastForwardStrategy = FastForwardStrategy.Default,
					SkipReuc = true,
					OnCheckoutProgress = CheckoutProgressHandler(progressReporter)
				});

				cancellationToken.ThrowIfCancellationRequested();

				if (result.Status == MergeStatus.Conflicts)
				{
					logger.LogDebug("Merge conflict, aborting and reverting to {0}", oldHead.FriendlyName);
					repository.Reset(ResetMode.Hard, oldTip, new CheckoutOptions
					{
						OnCheckoutProgress = CheckoutProgressHandler(progressReporter)
					});
					cancellationToken.ThrowIfCancellationRequested();
				}

				repository.RemoveUntrackedFiles();
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);

			if (result.Status == MergeStatus.Conflicts)
			{
				await eventConsumer.HandleEvent(EventType.RepoMergeConflict, new List<string> { oldTip.Sha, trackedBranch.Tip.Sha, oldHead.FriendlyName ?? UnknownReference, trackedBranch.FriendlyName }, cancellationToken).ConfigureAwait(false);
				return null;
			}

			return result.Status == MergeStatus.FastForward;
		}

		/// <inheritdoc />
		public async Task<bool> Sychronize(string username, string password, string committerName, string committerEmail, Action<int> progressReporter, bool synchronizeTrackedBranch, CancellationToken cancellationToken)
		{
			if (committerName == null)
				throw new ArgumentNullException(nameof(committerName));
			if (committerEmail == null)
				throw new ArgumentNullException(nameof(committerEmail));
			if (progressReporter == null)
				throw new ArgumentNullException(nameof(progressReporter));

			if (username == null && password == null)
			{
				logger.LogTrace("Not synchronizing due to lack of credentials!");
				return false;
			}

			logger.LogTrace("Begin Synchronize...");

			if (username == null)
				throw new ArgumentNullException(nameof(username));
			if (password == null)
				throw new ArgumentNullException(nameof(password));

			var startHead = Head;

			logger.LogTrace("Configuring <{0} ({1})> as author/committer", committerName, committerEmail);
			await Task.Factory.StartNew(() =>
			{
				repository.Config.Set("user.name", committerName);
				cancellationToken.ThrowIfCancellationRequested();
				repository.Config.Set("user.email", committerEmail);
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				if (!await eventConsumer.HandleEvent(
					EventType.RepoPreSynchronize,
					new List<string>
					{
						ioMananger.ResolvePath()
					},
					cancellationToken)
					.ConfigureAwait(false))
				{
					logger.LogDebug("Aborted synchronize due to event handler response!");
					return false;
				}
			}
			finally
			{
				logger.LogTrace("Resetting and cleaning untracked files...");
				await Task.Factory.StartNew(() =>
				{
					repository.RemoveUntrackedFiles();
					cancellationToken.ThrowIfCancellationRequested();
					repository.Reset(ResetMode.Hard, repository.Head.Tip, new CheckoutOptions
					{
						OnCheckoutProgress = CheckoutProgressHandler(progress => progressReporter(progress / 10))
					});
				}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
			}

			void FinalReporter(int progress) => progressReporter((int)(((float)progress) / 100 * 90));

			if (!synchronizeTrackedBranch)
			{
				await PushHeadToTemporaryBranch(username, password, FinalReporter, cancellationToken).ConfigureAwait(false);
				return false;
			}

			var sameHead = Head == startHead;
			if (sameHead || !Tracking)
			{
				logger.LogTrace("Aborted synchronize due to {0}!", sameHead ? "lack of changes" : "not being on tracked reference");
				return false;
			}

			logger.LogInformation("Synchronizing with origin...");

			return await Task.Factory.StartNew(() =>
			{
				var remote = repository.Network.Remotes.First();
				try
				{
					repository.Network.Push(repository.Head, GeneratePushOptions(FinalReporter, username, password, cancellationToken));
					return true;
				}
				catch (NonFastForwardException)
				{
					logger.LogInformation("Synchronize aborted, non-fast forward!");
					return false;
				}
				catch (UserCancelledException e)
				{
					cancellationToken.ThrowIfCancellationRequested();
					throw new InvalidOperationException("Caught UserCancelledException without cancellationToken triggering", e);
				}
				catch (LibGit2SharpException e)
				{
					logger.LogWarning("Unable to make synchronization push! Exception: {0}", e);
					return false;
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public Task<bool> IsSha(string committish, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			// check if it's a tag
			var gitObject = repository.Lookup(committish, ObjectType.Tag);
			if (gitObject != null)
				return false;
			cancellationToken.ThrowIfCancellationRequested();

			// check if it's a branch
			if (repository.Branches[committish] != null)
				return false;
			cancellationToken.ThrowIfCancellationRequested();

			// err on the side of references, if we can't look it up, assume its a reference
			if (repository.Lookup<Commit>(committish) != null)
				return true;
			return false;
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);
	}
}
