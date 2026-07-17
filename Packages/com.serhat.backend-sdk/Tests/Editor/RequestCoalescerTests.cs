#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Coalescing;
using NUnit.Framework;

namespace Serhat.Backend.Tests
{
    [TestFixture]
    public class RequestCoalescerTests
    {
        private TestLogger _logger = null!;
        private RequestCoalescer _coalescer = null!;

        [SetUp]
        public void Setup()
        {
            _logger = new TestLogger();
            _coalescer = new RequestCoalescer(_logger, TimeSpan.FromSeconds(5));
        }

        [TearDown]
        public void TearDown()
        {
            _coalescer.Dispose();
        }

        [Test]
        public async Task ExecuteAsync_SingleRequest_ExecutesNormally()
        {
            var callCount = 0;

            var result = await _coalescer.ExecuteAsync(
                "key1",
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Success("result"));
                },
                "corr-1");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.EqualTo("result"));
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_ConcurrentRequests_CoalescesIntoOne()
        {
            var callCount = 0;
            var tcs = new TaskCompletionSource<bool>();

            // Start request that will block
            var task1 = _coalescer.ExecuteAsync(
                "key1",
                async ct =>
                {
                    callCount++;
                    await tcs.Task;
                    return CloudResult<string>.Success("shared-result");
                },
                "corr-1");

            // Small delay to ensure first task starts
            await Task.Delay(10);

            // Start second request with same key
            var task2 = _coalescer.ExecuteAsync(
                "key1",
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Success("should-not-execute"));
                },
                "corr-2");

            // Release the first request
            tcs.SetResult(true);

            var result1 = await task1;
            var result2 = await task2;

            Assert.That(callCount, Is.EqualTo(1), "Should only execute once");
            Assert.That(result1.Data, Is.EqualTo("shared-result"));
            Assert.That(result2.Data, Is.EqualTo("shared-result"));
        }

        [Test]
        public async Task ExecuteAsync_DifferentKeys_ExecutesSeparately()
        {
            var callCount = 0;

            var task1 = _coalescer.ExecuteAsync(
                "key1",
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return Task.FromResult(CloudResult<string>.Success("result1"));
                },
                "corr-1");

            var task2 = _coalescer.ExecuteAsync(
                "key2",
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return Task.FromResult(CloudResult<string>.Success("result2"));
                },
                "corr-2");

            await Task.WhenAll(task1, task2);

            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(task1.Result.Data, Is.EqualTo("result1"));
            Assert.That(task2.Result.Data, Is.EqualTo("result2"));
        }

        [Test]
        public async Task ExecuteAsync_SequentialRequests_ExecutesSeparately()
        {
            var callCount = 0;

            var result1 = await _coalescer.ExecuteAsync(
                "key1",
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Success("first"));
                },
                "corr-1");

            // Wait for cleanup
            await Task.Delay(200);

            var result2 = await _coalescer.ExecuteAsync(
                "key1",
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Success("second"));
                },
                "corr-2");

            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(result1.Data, Is.EqualTo("first"));
            Assert.That(result2.Data, Is.EqualTo("second"));
        }

        [Test]
        public async Task ExecuteAsync_FailedRequest_PropagatesError()
        {
            var tcs = new TaskCompletionSource<bool>();

            var task1 = _coalescer.ExecuteAsync(
                "key1",
                async ct =>
                {
                    await tcs.Task;
                    return CloudResult<string>.Failure(new BackendError(
                        ErrorCodes.ServiceUnavailable, "Service down", retryable: true));
                },
                "corr-1");

            await Task.Delay(10);

            var task2 = _coalescer.ExecuteAsync(
                "key1",
                ct => Task.FromResult(CloudResult<string>.Success("should-not-run")),
                "corr-2");

            tcs.SetResult(true);

            var result1 = await task1;
            var result2 = await task2;

            Assert.That(result1.IsSuccess, Is.False);
            Assert.That(result2.IsSuccess, Is.False);
            Assert.That(result1.Error!.Code, Is.EqualTo(ErrorCodes.ServiceUnavailable));
            Assert.That(result2.Error!.Code, Is.EqualTo(ErrorCodes.ServiceUnavailable));
        }

        [Test]
        public void InFlightCount_TracksActiveRequests()
        {
            Assert.That(_coalescer.InFlightCount, Is.EqualTo(0));
        }
    }
}
