using System;
using System.Collections.Generic;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Fine-grained territory ownership, stored as a flat grid of owner IDs
/// (0 = unclaimed/wild). This is the whole foundation of the border system:
/// borders are never stored as shapes, only derived from where owner IDs
/// differ between neighbouring cells (see shaders/border_outline.gdshader for
/// rendering). Sieges, cutoff territory and negotiation regions all reduce to
/// grid connectivity - FloodFillRegion() and IsContiguous().
/// </summary>
public sealed class OwnershipGrid
{
    public int Width { get; }
    public int Height { get; }
    public int[] Cells { get; set; }

    public OwnershipGrid(int width, int height)
    {
        Width = width;
        Height = height;
        Cells = new int[width * height];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Owner id at (x, y), or -1 outside the grid.</summary>
    public int GetOwner(int x, int y) => InBounds(x, y) ? Cells[y * Width + x] : -1;

    public void SetOwner(int x, int y, int ownerId)
    {
        if (InBounds(x, y))
            Cells[y * Width + x] = ownerId;
    }

    public void FillRect(int x0, int y0, int x1, int y1, int ownerId)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(Width, x1); x++)
                Cells[y * Width + x] = ownerId;
    }

    /// <summary>
    /// The 4-connected region of same-owner cells containing (startX, startY),
    /// found iteratively (no recursion depth limit).
    /// </summary>
    public List<Vector2I> FloodFillRegion(int startX, int startY)
    {
        var region = new List<Vector2I>();
        int owner = GetOwner(startX, startY);
        if (owner == -1)
            return region;
        var visited = new bool[Cells.Length];
        var stack = new Stack<int>();
        stack.Push(startY * Width + startX);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (visited[i] || Cells[i] != owner)
                continue;
            visited[i] = true;
            int x = i % Width, y = i / Width;
            region.Add(new Vector2I(x, y));
            if (x + 1 < Width) stack.Push(i + 1);
            if (x > 0) stack.Push(i - 1);
            if (y + 1 < Height) stack.Push(i + Width);
            if (y > 0) stack.Push(i - Width);
        }
        return region;
    }

    /// <summary>
    /// True if every cell owned by ownerId is reachable from every other (the
    /// realm isn't split into disconnected pockets).
    /// </summary>
    public bool IsContiguous(int ownerId)
    {
        int first = Array.IndexOf(Cells, ownerId);
        if (first < 0)
            return true;
        int total = 0;
        foreach (int c in Cells)
            if (c == ownerId)
                total++;
        return FloodFillRegion(first % Width, first / Width).Count == total;
    }
}
