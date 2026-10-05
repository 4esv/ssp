namespace Ssp.Web.Editing;

/// <summary>
/// Tells a long press from a tap and a drag. One pointer that stays within <see cref="Slop"/> pixels for <see cref="Hold"/>
/// is a long press, and the callback runs then. A move past the slop, a second pointer or the end of the press stops the wait.
/// </summary>
public sealed class LongPress(TimeProvider clock, Action held) : IDisposable
{
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(500);

    // NOTE: A finger that holds still drifts a few pixels. Under this many it is still a press, not a drag.
    public const double Slop = 10;

    ITimer? timer;
    long? pointer;
    (double X, double Y) from;

    /// <summary>True when the last press was a long press. The next press clears it.</summary>
    public bool Fired { get; private set; }

    public void Down(long id, double x, double y)
    {
        // NOTE: A second finger makes a pinch or a pan, not a long press.
        if (pointer is not null && pointer != id)
        {
            Stop();
            return;
        }
        Stop();
        Fired = false;
        (pointer, from) = (id, (x, y));
        timer = clock.CreateTimer(_ => Fire(id), null, Hold, Timeout.InfiniteTimeSpan);
    }

    public void Move(long id, double x, double y)
    {
        if (pointer != id || Fired) return;
        if (Math.Abs(x - from.X) > Slop || Math.Abs(y - from.Y) > Slop) Stop();
    }

    /// <summary>Ends the press of a pointer. True when it was a long press.</summary>
    public bool Up(long id)
    {
        if (pointer != id) return false;
        Stop();
        return Fired;
    }

    public void Dispose() => Stop();

    void Fire(long id)
    {
        if (pointer != id || Fired) return;
        Fired = true;
        held();
    }

    void Stop()
    {
        timer?.Dispose();
        (timer, pointer) = (null, null);
    }
}
