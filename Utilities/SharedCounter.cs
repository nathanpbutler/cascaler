namespace cascaler.Utilities;

/// <summary>
/// Thread-safe counter for tracking progress across multiple threads.
/// </summary>
public class SharedCounter
{
    private int _value;

    public int Increment() => Interlocked.Increment(ref _value);
    public int Value => _value;
}
