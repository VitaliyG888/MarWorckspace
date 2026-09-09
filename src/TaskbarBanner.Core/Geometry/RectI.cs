namespace TaskbarBanner.Core.Geometry;

public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public long Area => (long)Width * Height;

    public static RectI FromLtrb(int left, int top, int right, int bottom)
        => new(left, top, right - left, bottom - top);

    public RectI Intersect(RectI other)
    {
        int l = Math.Max(Left, other.Left);
        int t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return l < r && t < b ? FromLtrb(l, t, r, b) : default;
    }

    public bool Contains(RectI other)
        => Left <= other.Left && Top <= other.Top && Right >= other.Right && Bottom >= other.Bottom;

    public double OverlapFraction(RectI other)
        => Area == 0 ? 0 : (double)Intersect(other).Area / Area;
}
