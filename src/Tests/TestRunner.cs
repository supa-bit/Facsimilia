using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Base for headless tests, run as
///   godot --headless --path . --script res://src/Tests/&lt;Name&gt;.cs
/// Failed checks are printed and counted; the process exits 1 if any
/// failed, instead of stopping in the debugger the way a failed assert does.
/// </summary>
public abstract partial class TestRunner : SceneTree
{
    int _failures;

    protected bool Check(bool ok, string message)
    {
        if (!ok)
        {
            GD.PrintErr("FAIL: ", message);
            _failures++;
        }
        return ok;
    }

    protected static bool Near(int value, int expected, int tolerance = 2) =>
        System.Math.Abs(value - expected) <= tolerance;

    /// <summary>Prints the summary if everything passed, then exits with 0 or 1.</summary>
    protected void Finish(string passedSummary)
    {
        if (_failures == 0)
            GD.Print(passedSummary);
        else
            GD.PrintErr($"{_failures} check(s) failed");
        Quit(_failures == 0 ? 0 : 1);
    }
}
