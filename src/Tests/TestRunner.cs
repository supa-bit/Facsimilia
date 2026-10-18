using System;
using System.Threading.Tasks;
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

    /// <summary>
    /// Runs Run() and exits. An exception counts as a failure (and still
    /// exits) instead of leaving the process hanging. Tests either override
    /// Run() and end with Finish(), or override _Initialize() themselves.
    /// </summary>
    public override async void _Initialize()
    {
        try
        {
            // Each test saves into its own empty folder, never into the player's
            // real save slots, and never into another test's (tests may run at once).
            string root = $"user://test_saves/{GetType().Name}";
            World.SaveSystem.DeleteDir(root);
            DirAccess.MakeDirRecursiveAbsolute(root);
            World.SaveSystem.Root = root;
            await Run();
        }
        catch (Exception e)
        {
            Check(false, "unhandled exception: " + e);
            Finish("");
        }
    }

    protected virtual Task Run() => Task.CompletedTask;

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
        // Let C# finish releasing Godot objects (random number generators and the like) while the engine
        // still runs: left to the very end, one now and then aborts Godot's exit (exit code 134) after a pass.
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        Quit(_failures == 0 ? 0 : 1);
    }
}
