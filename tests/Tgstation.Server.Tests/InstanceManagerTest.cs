﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Rights;
using Tgstation.Server.Client;
using Tgstation.Server.Host.Controllers;
using Tgstation.Server.Tests.Instance;

namespace Tgstation.Server.Tests
{
	sealed class InstanceManagerTest
	{
		readonly IInstanceManagerClient instanceManagerClient;
		readonly IUsersClient usersClient;
		readonly string testRootPath;

		long counter;

		public InstanceManagerTest(IInstanceManagerClient instanceManagerClient, IUsersClient usersClient, string testRootPath)
		{
			this.instanceManagerClient = instanceManagerClient ?? throw new ArgumentNullException(nameof(instanceManagerClient));
			this.usersClient = usersClient ?? throw new ArgumentNullException(nameof(usersClient));
			this.testRootPath = testRootPath ?? throw new ArgumentNullException(nameof(testRootPath));

			counter = 0;
		}

		Task<Api.Models.Instance> CreateTestInstance(CancellationToken cancellationToken) => instanceManagerClient.CreateOrAttach(new Api.Models.Instance
		{
			Name = "TestInstance-" + ++counter,
			Path = Path.Combine(testRootPath, Guid.NewGuid().ToString()),
			Online = true,
			ChatBotLimit = 2
		}, cancellationToken);

		public async Task Run(CancellationToken cancellationToken)
		{
			var firstTest = await CreateTestInstance(cancellationToken).ConfigureAwait(false);
			//instances always start offline
			Assert.AreEqual(false, firstTest.Online);
			//check it exists
			Assert.IsTrue(Directory.Exists(firstTest.Path));

			//cant create instances in existent directories
			var testNonEmpty = Path.Combine(testRootPath, Guid.NewGuid().ToString());
			Directory.CreateDirectory(testNonEmpty);
			var testFile = Path.Combine(testNonEmpty, "asdf");
			await File.WriteAllBytesAsync(testFile, Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.CreateOrAttach(new Api.Models.Instance
			{
				Path = testNonEmpty,
				Name = "NonEmptyTest"
			}, cancellationToken), ErrorCode.InstanceAtExistingPath).ConfigureAwait(false);

			//check it works for truly empty directories
			File.Delete(testFile);
			var secondTry = await instanceManagerClient.CreateOrAttach(new Api.Models.Instance
			{
				Path = Path.Combine(testRootPath, Guid.NewGuid().ToString()),
				Name = "NonEmptyTest"
			}, cancellationToken).ConfigureAwait(false);

			await Assert.ThrowsExceptionAsync<ConflictException>(() => instanceManagerClient.CreateOrAttach(firstTest, cancellationToken)).ConfigureAwait(false);

			//can't create instances in installation directory
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.CreateOrAttach(new Api.Models.Instance
			{
				Path = "./A/Local/Path",
				Name = "NoInstallDirTest"
			}, cancellationToken), ErrorCode.InstanceAtConflictingPath).ConfigureAwait(false);

			//can't create instances as children of other instances
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.CreateOrAttach(new Api.Models.Instance
			{
				Path = Path.Combine(firstTest.Path, "subdir"),
				Name = "NoOtherInstanceDirTest"
			}, cancellationToken), ErrorCode.InstanceAtConflictingPath).ConfigureAwait(false);

			//can't move to existent directories
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.Update(new Api.Models.Instance
			{
				Id = firstTest.Id,
				Path = testNonEmpty
			}, cancellationToken), ErrorCode.InstanceAtExistingPath).ConfigureAwait(false);

			// test can't create instance outside of whitelist
			await ApiAssert.ThrowsException<ApiConflictException>(() => instanceManagerClient.CreateOrAttach(new Api.Models.Instance
			{
				Name = "TestInstanceOutsideOfWhitelist",
				Path = Path.Combine(testRootPath, "..", Guid.NewGuid().ToString()),
				Online = true,
				ChatBotLimit = 1
			}, cancellationToken), ErrorCode.InstanceNotAtWhitelistedPath);

			//test basic move
			Directory.Delete(testNonEmpty);
			var initialPath = firstTest.Path;
			firstTest = await instanceManagerClient.Update(new Api.Models.Instance
			{
				Id = firstTest.Id,
				Path = testNonEmpty
			}, cancellationToken).ConfigureAwait(false);

			Assert.IsNotNull(firstTest.MoveJob);

			do
			{
				firstTest = await instanceManagerClient.GetId(firstTest, cancellationToken).ConfigureAwait(false);
				await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
			} while (firstTest.MoveJob != null);


			//online it for real for component tests
			firstTest.Online = true;
			firstTest.ConfigurationType = ConfigurationType.HostWrite;
			firstTest = await instanceManagerClient.Update(firstTest, cancellationToken).ConfigureAwait(false);
			Assert.AreEqual(true, firstTest.Online);
			Assert.AreEqual(ConfigurationType.HostWrite, firstTest.ConfigurationType);

			//can't move online instance
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.Update(new Api.Models.Instance
			{
				Id = firstTest.Id,
				Path = initialPath
			}, cancellationToken), ErrorCode.InstanceRelocateOnline).ConfigureAwait(false);

			var instanceClient = instanceManagerClient.CreateClient(firstTest);
			var testSuite1 = new InstanceTest(instanceClient, instanceManagerClient);
			await testSuite1.RunTests(cancellationToken).ConfigureAwait(false);

			//can regain permissions on instance without instance user
			var ourInstanceUser = await instanceClient.Users.Read(cancellationToken).ConfigureAwait(false);
			await instanceClient.Users.Delete(ourInstanceUser, cancellationToken).ConfigureAwait(false);

			await Assert.ThrowsExceptionAsync<InsufficientPermissionsException>(() => instanceClient.Users.Read(cancellationToken)).ConfigureAwait(false);

			await instanceManagerClient.Update(new Api.Models.Instance
			{
				Id = firstTest.Id
			}, cancellationToken).ConfigureAwait(false);
			ourInstanceUser = await instanceClient.Users.Read(cancellationToken).ConfigureAwait(false);

			//can't detach online instance
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.Detach(firstTest, cancellationToken), ErrorCode.InstanceDetachOnline).ConfigureAwait(false);

			firstTest.Online = false;
			firstTest = await instanceManagerClient.Update(firstTest, cancellationToken).ConfigureAwait(false);

			await instanceManagerClient.Detach(firstTest, cancellationToken).ConfigureAwait(false);

			var instanceAttachFileName = (string)typeof(InstanceController).GetField("InstanceAttachFileName", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
			var attachPath = Path.Combine(firstTest.Path, instanceAttachFileName);
			Assert.IsTrue(File.Exists(attachPath));

			//can recreate detached instance
			firstTest = await instanceManagerClient.CreateOrAttach(firstTest, cancellationToken).ConfigureAwait(false);

			// Test updating only with SetChatBotLimit works
			var current = await usersClient.Read(cancellationToken);
			var update = new UserUpdate
			{
				Id = current.Id,
				InstanceManagerRights = InstanceManagerRights.SetChatBotLimit
			};
			await usersClient.Update(update, cancellationToken);
			firstTest.ChatBotLimit = 77;
			var newThing = await instanceManagerClient.Update(firstTest, cancellationToken);
			Assert.AreEqual(77, newThing.ChatBotLimit);

			update.InstanceManagerRights |= InstanceManagerRights.Delete;
			await usersClient.Update(update, cancellationToken);

			//but only if the attach file exists
			await instanceManagerClient.Detach(firstTest, cancellationToken).ConfigureAwait(false);
			File.Delete(attachPath);
			await ApiAssert.ThrowsException<ConflictException>(() => instanceManagerClient.CreateOrAttach(firstTest, cancellationToken), ErrorCode.InstanceAtExistingPath).ConfigureAwait(false);
		}
	}
}
