namespace Ssp.Web.Tests;

/// <summary>A clock that moves only when the test calls <see cref="Advance"/>. Timers fire then and not before.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    readonly object gate = new();
    readonly List<ManualTimer> timers = [];
    long now;

    public override long GetTimestamp() { lock (gate) return now; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = Interlocked.Add(ref now, by.Ticks);
        while (true)
        {
            ManualTimer? due;
            lock (gate) due = timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
            if (due is null) return;
            due.Fire();
        }
    }

    sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        TimeSpan period = Timeout.InfiniteTimeSpan;
        public long Due { get; private set; } = long.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
                period = newPeriod;
                if (dueTime == Timeout.InfiniteTimeSpan) return true;
                Due = owner.now + dueTime.Ticks;
                owner.timers.Add(this);
            }
            return true;
        }

        public void Fire()
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
                if (period > TimeSpan.Zero) { Due += period.Ticks; owner.timers.Add(this); }
            }
            callback(state);
        }

        public void Dispose() { lock (owner.gate) owner.timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
