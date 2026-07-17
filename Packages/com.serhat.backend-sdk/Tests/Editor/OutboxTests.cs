#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Outbox;
using NUnit.Framework;

namespace Serhat.Backend.Tests
{
    [TestFixture]
    public class OutboxTests
    {
        private TestClock _clock = null!;
        private TestRandom _random = null!;
        private TestLogger _logger = null!;
        private InMemoryStorage _storage = null!;
        private ISerializer _serializer = null!;
        private OutboxOptions _outboxOptions = null!;
        private RetryOptions _retryOptions = null!;
        private PersistentOutbox _outbox = null!;

        [SetUp]
        public async Task Setup()
        {
            _clock = new TestClock();
            _random = new TestRandom();
            _logger = new TestLogger();
            _storage = new InMemoryStorage();
            _serializer = new UnityJsonSerializer();
            _outboxOptions = new OutboxOptions
            {
                Enabled = true,
                MaxQueueSize = 10,
                MaxCommandAttempts = 3
            };
            _retryOptions = new RetryOptions
            {
                InitialDelay = TimeSpan.FromMilliseconds(100),
                BackoffMultiplier = 2.0,
                JitterFactor = 0.0
            };
            _outbox = new PersistentOutbox(
                _outboxOptions,
                _retryOptions,
                _storage,
                _serializer,
                _clock,
                _random,
                _logger);

            await _outbox.LoadAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _outbox.Dispose();
        }

        [Test]
        public async Task Enqueue_AddsCommandToQueue()
        {
            var result = await _outbox.EnqueueAsync(
                "TestFunction",
                new TestRequest { Value = "test" },
                Guid.NewGuid(),
                "corr-123");

            Assert.That(result.IsSuccess, Is.True);

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Enqueue_RejectsWhenQueueFull()
        {
            // Fill the queue
            for (int i = 0; i < _outboxOptions.MaxQueueSize; i++)
            {
                await _outbox.EnqueueAsync(
                    "TestFunction",
                    new TestRequest { Value = $"test-{i}" },
                    Guid.NewGuid(),
                    $"corr-{i}");
            }

            // Try to add one more
            var result = await _outbox.EnqueueAsync(
                "TestFunction",
                new TestRequest { Value = "overflow" },
                Guid.NewGuid(),
                "corr-overflow");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCodes.OutboxFull));
        }

        [Test]
        public async Task Dequeue_ReturnsOldestReadyCommand()
        {
            await _outbox.EnqueueAsync("Func1", new TestRequest(), Guid.NewGuid(), "c1");
            _clock.Advance(TimeSpan.FromSeconds(1));
            await _outbox.EnqueueAsync("Func2", new TestRequest(), Guid.NewGuid(), "c2");

            var command = await _outbox.DequeueAsync();

            Assert.That(command, Is.Not.Null);
            Assert.That(command!.FunctionName, Is.EqualTo("Func1"));
            Assert.That(command.Status, Is.EqualTo(OutboxCommandStatus.InProgress));
        }

        [Test]
        public async Task Dequeue_RespectsNextAttemptTime()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest(), Guid.NewGuid(), "c1");

            var cmd = await _outbox.DequeueAsync();
            Assert.That(cmd, Is.Not.Null);

            // Fail it to schedule retry
            await _outbox.FailAsync(cmd!.CommandId, "ERROR", "Test error", retryable: true);

            // Should not be ready yet
            var cmd2 = await _outbox.DequeueAsync();
            Assert.That(cmd2, Is.Null);

            // Advance time past retry delay
            _clock.Advance(TimeSpan.FromSeconds(1));

            var cmd3 = await _outbox.DequeueAsync();
            Assert.That(cmd3, Is.Not.Null);
        }

        [Test]
        public async Task Complete_RemovesCommandFromQueue()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest(), Guid.NewGuid(), "c1");

            var cmd = await _outbox.DequeueAsync();
            await _outbox.CompleteAsync(cmd!.CommandId);

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Fail_MovesToDeadLetterAfterMaxAttempts()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest(), Guid.NewGuid(), "c1");

            // Exhaust all attempts
            for (int i = 0; i < _outboxOptions.MaxCommandAttempts; i++)
            {
                _clock.Advance(TimeSpan.FromMinutes(1)); // Advance past backoff
                var cmd = await _outbox.DequeueAsync();
                if (cmd != null)
                {
                    await _outbox.FailAsync(cmd.CommandId, "ERROR", "Test", retryable: true);
                }
            }

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(0));
            Assert.That(status.DeadLetterCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Fail_MovesToDeadLetterOnNonRetryable()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest(), Guid.NewGuid(), "c1");

            var cmd = await _outbox.DequeueAsync();
            await _outbox.FailAsync(cmd!.CommandId, ErrorCodes.ValidationFailed, "Invalid", retryable: false);

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(0));
            Assert.That(status.DeadLetterCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RetryDeadLetter_MovesBackToQueue()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest(), Guid.NewGuid(), "c1");

            var cmd = await _outbox.DequeueAsync();
            await _outbox.FailAsync(cmd!.CommandId, "ERROR", "Test", retryable: false);

            var deadLetters = _outbox.GetDeadLetters();
            Assert.That(deadLetters.Count, Is.EqualTo(1));

            await _outbox.RetryDeadLetterAsync(cmd.CommandId);

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(1));
            Assert.That(status.DeadLetterCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Persistence_SurvivesReload()
        {
            await _outbox.EnqueueAsync("TestFunc", new TestRequest { Value = "persist" }, Guid.NewGuid(), "c1");

            // Create new outbox instance with same storage
            _outbox.Dispose();
            _outbox = new PersistentOutbox(
                _outboxOptions,
                _retryOptions,
                _storage,
                _serializer,
                _clock,
                _random,
                _logger);

            await _outbox.LoadAsync();

            var status = _outbox.GetStatus();
            Assert.That(status.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void Serializer_RoundTripsOutboxPropertiesAndFrameworkValues()
        {
            var timestamp = new DateTime(2026, 7, 17, 8, 30, 0, DateTimeKind.Utc);
            var state = new OutboxState
            {
                LastModifiedUtc = timestamp,
                PendingCommands = new List<OutboxCommand>
                {
                    new()
                    {
                        CommandId = "command-1",
                        FunctionName = "TestFunc",
                        CreatedAtUtc = timestamp,
                        NextAttemptAtUtc = timestamp.AddMinutes(1),
                        Status = OutboxCommandStatus.Pending
                    }
                }
            };

            var json = _serializer.Serialize(state);
            var restored = _serializer.Deserialize<OutboxState>(json);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.LastModifiedUtc, Is.EqualTo(timestamp));
            Assert.That(restored.PendingCommands, Has.Count.EqualTo(1));
            Assert.That(restored.PendingCommands[0].FunctionName, Is.EqualTo("TestFunc"));
            Assert.That(restored.PendingCommands[0].NextAttemptAtUtc,
                Is.EqualTo(timestamp.AddMinutes(1)));
        }
    }

    // Test doubles
    internal class InMemoryStorage : IStorage
    {
        private readonly Dictionary<string, string> _data = new();

        public Task<string?> ReadAsync(string key, CancellationToken ct = default)
        {
            _data.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task WriteAsync(string key, string data, CancellationToken ct = default)
        {
            _data[key] = data;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            _data.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        {
            return Task.FromResult(_data.ContainsKey(key));
        }
    }

    [Serializable]
    internal class TestRequest
    {
        public string Value = "";
    }
}
