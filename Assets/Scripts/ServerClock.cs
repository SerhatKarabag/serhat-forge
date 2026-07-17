using System;
using System.Diagnostics;
using Serhat.Backend.Core;

/// <summary>
/// Monotonic, server-anchored replacement for <see cref="DateTime.UtcNow"/>.
///
/// Anchors to the authoritative server UTC carried in every <c>ResponseEnvelope</c>
/// (see <see cref="IServerTimeAnchor"/>) and advances from that anchor using
/// <see cref="Stopwatch.GetTimestamp"/>, which is a strict OS-level monotonic counter
/// (QueryPerformanceCounter on Windows, mach_absolute_time on iOS/macOS,
/// clock_gettime(CLOCK_MONOTONIC) on Android/Linux). That counter is immune to device
/// clock changes, so fast-forwarding the system clock cannot unlock daily gifts,
/// cooldowns, or any other time-gated feature on the client UX side.
///
/// Lifecycle:
/// - A single instance is created and passed into the backend SDK as its
///   <see cref="IClock"/>. The SDK transport layer calls
///   <see cref="AnchorToServerTime"/> after every successful envelope deserialization.
/// - Application code should read <see cref="Instance"/>.<see cref="UtcNow"/> anywhere
///   it previously used <see cref="DateTime.UtcNow"/> for server-relative decisions.
/// - Before the first response arrives, <see cref="UtcNow"/> falls back to
///   <see cref="DateTime.UtcNow"/>. This is only a concern during bootstrap
///   (pre-login) and never affects daily-gift / cooldown checks which run
///   post-bootstrap.
///
/// Thread-safety: all reads and writes are guarded by a lock. The invoker may call
/// <see cref="AnchorToServerTime"/> from a background async continuation while
/// gameplay code reads <see cref="UtcNow"/> from the main thread.
/// </summary>
public sealed class ServerClock : IClock, IServerTimeAnchor
{
    /// <summary>Shared instance consumed by the backend SDK and application code.</summary>
    public static ServerClock Instance { get; } = new ServerClock();

    private static readonly double StopwatchTicksPerSecond = Stopwatch.Frequency;

    private readonly object _sync = new object();
    private DateTime _anchorServerUtc;
    private long _anchorStopwatchTicks;
    private bool _isAnchored;

    /// <summary>
    /// Returns the estimated current server UTC.
    ///
    /// Uses <see cref="Stopwatch.GetTimestamp"/> as a monotonic delta from the last
    /// anchor point; this value is not affected by users changing their device clock.
    /// Falls back to <see cref="DateTime.UtcNow"/> if no anchor has been received yet.
    /// </summary>
    public DateTime UtcNow
    {
        get
        {
            lock (_sync)
            {
                if (!_isAnchored)
                {
                    return DateTime.UtcNow;
                }

                long elapsedTicks = Stopwatch.GetTimestamp() - _anchorStopwatchTicks;
                if (elapsedTicks < 0L)
                {
                    elapsedTicks = 0L;
                }

                double elapsedSeconds = elapsedTicks / StopwatchTicksPerSecond;
                return _anchorServerUtc.AddSeconds(elapsedSeconds);
            }
        }
    }

    /// <summary>
    /// Unix-ms timestamp derived from <see cref="UtcNow"/>. Used by the SDK when
    /// stamping outbound requests, so request timestamps also respect the server anchor.
    /// </summary>
    public long TimestampMs => new DateTimeOffset(UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>True once the transport has received at least one response with a server time.</summary>
    public bool IsAnchored
    {
        get
        {
            lock (_sync)
            {
                return _isAnchored;
            }
        }
    }

    /// <inheritdoc />
    public void AnchorToServerTime(DateTime serverUtcNow)
    {
        if (serverUtcNow == default)
        {
            return;
        }

        // Normalize to UTC so a naive Kind=Unspecified value from JSON doesn't drift our delta.
        if (serverUtcNow.Kind != DateTimeKind.Utc)
        {
            serverUtcNow = DateTime.SpecifyKind(serverUtcNow, DateTimeKind.Utc);
        }

        long stopwatchNow = Stopwatch.GetTimestamp();

        lock (_sync)
        {
            _anchorServerUtc = serverUtcNow;
            _anchorStopwatchTicks = stopwatchNow;
            _isAnchored = true;
        }
    }
}
