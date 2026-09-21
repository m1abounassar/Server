namespace SandboxServer;

internal interface IChance
{
    double NextDouble();
    float NextRange(float minInclusive, float maxInclusive);
}

internal sealed class SystemChance : IChance
{
    private readonly Random _random;

    public SystemChance()
        : this(Random.Shared)
    {
    }

    public SystemChance(Random random)
    {
        _random = random;
    }

    public double NextDouble() => _random.NextDouble();

    public float NextRange(float minInclusive, float maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        return minInclusive + (float)_random.NextDouble() * (maxInclusive - minInclusive);
    }
}

/// <summary>
/// Test double: queued NextDouble values, then 0. Returns mid-range for NextRange unless queued.
/// </summary>
internal sealed class ScriptedChance : IChance
{
    private readonly Queue<double> _doubles = new();
    private readonly Queue<float> _ranges = new();

    public ScriptedChance(params double[] doubles)
    {
        foreach (double value in doubles)
        {
            _doubles.Enqueue(value);
        }
    }

    public void EnqueueDouble(double value) => _doubles.Enqueue(value);

    public void EnqueueRange(float value) => _ranges.Enqueue(value);

    public double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 0d;

    public float NextRange(float minInclusive, float maxInclusive)
    {
        if (_ranges.Count > 0)
        {
            return _ranges.Dequeue();
        }

        return minInclusive + (maxInclusive - minInclusive) * 0.5f;
    }
}
