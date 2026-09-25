namespace Facsimilia.Game;

/// <summary>
/// A hash of a few numbers that is the same every time the game runs, for
/// seeding random draws (.NET's HashCode.Combine changes from run to run,
/// which made "the same year's weather" differ between runs).
/// </summary>
public static class StableHash
{
    public static int Of(params int[] values)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in values)
            {
                h = (h ^ (uint)v) * 16777619;
                h ^= h >> 15;
                h *= 0x2c1b3c6d;
                h ^= h >> 12;
            }
            return (int)(h & 0x7fffffff);
        }
    }
}
