#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Serhat.Backend.Core;
using Serhat.Backend.Core.Resilience;
using NUnit.Framework;

namespace Serhat.Backend.Tests
{
    [TestFixture]
    public class RetryPolicyTests
    {
        private TestClock _clock = null!;
        private TestRandom _random = null!;
        private TestLogger _logger = null!;
        private RetryOptions _options = null!;
        private RetryPolicy _policy = null!;

        [SetUp]
        public void Setup()
        {
            _clock = new TestClock();
            _random = new TestRandom(0.5); // Consistent jitter for testing
            _logger = new TestLogger();
            _options = new RetryOptions
            {
                MaxAttempts = 3,
                InitialDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromSeconds(10),
                BackoffMultiplier = 2.0,
                JitterFactor = 0.0 // Disable jitter for predictable tests
            };
            _policy = new RetryPolicy(_options, _clock, _random, _logger);
        }

        [Test]
        public async Task ExecuteAsync_SuccessOnFirstAttempt_ReturnsSuccess()
        {
            var callCount = 0;

            var result = await _policy.ExecuteAsync(
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Success("ok"));
                },
                "TestOp",
                "corr-123");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Data, Is.EqualTo("ok"));
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_RetryableFailure_RetriesAndSucceeds()
        {
            var callCount = 0;

            var result = await _policy.ExecuteAsync(
                ct =>
                {
                    callCount++;
                    if (callCount < 3)
                    {
                        return Task.FromResult(CloudResult<string>.Failure(
                            new BackendError(ErrorCodes.NetworkError, "Network error", retryable: true)));
                    }
                    return Task.FromResult(CloudResult<string>.Success("ok"));
                },
                "TestOp",
                "corr-123");

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(callCount, Is.EqualTo(3));
        }

        [Test]
        public async Task ExecuteAsync_NonRetryableFailure_DoesNotRetry()
        {
            var callCount = 0;

            var result = await _policy.ExecuteAsync(
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Failure(
                        new BackendError(ErrorCodes.ValidationFailed, "Invalid", retryable: false)));
                },
                "TestOp",
                "corr-123");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCodes.ValidationFailed));
            Assert.That(callCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteAsync_AllAttemptsExhausted_ReturnsFailure()
        {
            var callCount = 0;

            var result = await _policy.ExecuteAsync(
                ct =>
                {
                    callCount++;
                    return Task.FromResult(CloudResult<string>.Failure(
                        new BackendError(ErrorCodes.ServiceUnavailable, "Unavailable", retryable: true)));
                },
                "TestOp",
                "corr-123");

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(callCount, Is.EqualTo(_options.MaxAttempts));
        }

        [Test]
        public void CalculateDelay_ExponentialBackoff_CalculatesCorrectly()
        {
            // Attempt 1: 100ms
            var delay1 = _policy.CalculateDelay(1);
            Assert.That(delay1.TotalMilliseconds, Is.EqualTo(100).Within(1));

            // Attempt 2: 200ms (100 * 2^1)
            var delay2 = _policy.CalculateDelay(2);
            Assert.That(delay2.TotalMilliseconds, Is.EqualTo(200).Within(1));

            // Attempt 3: 400ms (100 * 2^2)
            var delay3 = _policy.CalculateDelay(3);
            Assert.That(delay3.TotalMilliseconds, Is.EqualTo(400).Within(1));
        }

        [Test]
        public void CalculateDelay_CapsAtMaxDelay()
        {
            _options.MaxDelay = TimeSpan.FromMilliseconds(300);
            _policy = new RetryPolicy(_options, _clock, _random, _logger);

            // Attempt 3 would be 400ms but should cap at 300ms
            var delay = _policy.CalculateDelay(3);
            Assert.That(delay.TotalMilliseconds, Is.EqualTo(300).Within(1));
        }

        [Test]
        public void ShouldRetry_ClassifiesErrorsCorrectly()
        {
            // Retryable errors
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.NetworkError, "", retryable: true)), Is.True);
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.Timeout, "", retryable: true)), Is.True);
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.ServiceUnavailable, "", retryable: true)), Is.True);
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.RateLimited, "", retryable: true)), Is.True);

            // Non-retryable errors
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.Unauthorized, "", retryable: false)), Is.False);
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.ValidationFailed, "", retryable: false)), Is.False);
            Assert.That(RetryPolicy.ShouldRetry(new BackendError(ErrorCodes.NotFound, "", retryable: false)), Is.False);
        }
    }

    // Test doubles
    internal class TestClock : IClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public long TimestampMs => new DateTimeOffset(UtcNow).ToUnixTimeMilliseconds();

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    internal class TestRandom : IRandom
    {
        private readonly double _fixedValue;

        public TestRandom(double fixedValue = 0.5)
        {
            _fixedValue = fixedValue;
        }

        public int Next(int minValue, int maxValue) => minValue + (int)((maxValue - minValue) * _fixedValue);
        public double NextDouble() => _fixedValue;
    }

    internal class TestLogger : IBackendLogger
    {
        public void Debug(string message, params object[] args) { }
        public void Info(string message, params object[] args) { }
        public void Warning(string message, params object[] args) { }
        public void Error(string message, Exception? exception = null, params object[] args) { }
    }
}
