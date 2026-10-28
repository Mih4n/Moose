namespace Moose.Core.Terminal.Parsing;

public sealed class CsiParameters
{
    public const int MaxCount = 32;
    const int MaxValue = 65535;

    readonly int[] values = new int[MaxCount];
    readonly bool[] subparameters = new bool[MaxCount];

    public int Count { get; private set; }

    public int this[int index] => index < Count ? values[index] : 0;

    public int Get(int index, int defaultValue) => index < Count && values[index] > 0 ? values[index] : defaultValue;

    public bool IsSubparameter(int index) => index < Count && subparameters[index];

    internal void Clear()
    {
        Array.Clear(values, 0, Count);
        Array.Clear(subparameters, 0, Count);
        Count = 0;
    }

    internal void AddDigit(int digit)
    {
        if (Count == 0) Count = 1;
        values[Count - 1] = Math.Min(values[Count - 1] * 10 + digit, MaxValue);
    }

    internal void Separate(bool colon)
    {
        if (Count == 0) Count = 1;
        if (Count == MaxCount) return;
        subparameters[Count] = colon;
        Count++;
    }
}
