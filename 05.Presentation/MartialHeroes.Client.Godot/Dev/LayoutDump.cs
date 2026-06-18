// Dev/LayoutDump.cs
//
// Headless LAYOUT ORACLE. When MH_DUMP_LAYOUT=1, prints the canvas-space rect + visibility of every
// Control in a subtree so front-end scene layouts can be verified NUMERICALLY against IDA without a
// screenshot. Canvas-space = accumulated local Position from the dump root down (so it matches the
// authored 1024x768 canvas coordinates the IDA builder uses), independent of the ScreenHost window
// fill-scale. Dev-only, gated; never affects release behaviour.

using Godot;

namespace MartialHeroes.Client.Godot.Dev;

/// <summary>
/// Numeric layout oracle for the front-end scenes (Phase A). Prints, per Control, its canvas-space
/// rect (x,y,w,h) + Visible/IsVisibleInTree, so the running layout can be diffed against the IDA
/// widget tables. Enabled only when the environment variable <c>MH_DUMP_LAYOUT=1</c>.
/// </summary>
public static class LayoutDump
{
    /// <summary>True when MH_DUMP_LAYOUT=1 — the only condition under which any dump is emitted.</summary>
    public static bool Enabled =>
        System.Environment.GetEnvironmentVariable("MH_DUMP_LAYOUT") == "1";

    /// <summary>
    /// Recursively prints every Control under <paramref name="root"/> in canvas-space coords.
    /// <paramref name="root"/> is treated as canvas origin (0,0) — pass the scene Control that
    /// ScreenHost mounts at canvas (0,0) (e.g. the LoginWindow / LoadingWindow / OpeningWindow).
    /// </summary>
    public static void Dump(Node root, string tag)
    {
        if (!Enabled || root is null) return;
        GD.Print($"[LAYOUTDUMP] ===== BEGIN {tag} =====");
        Walk(root, Vector2.Zero, tag, 0);
        GD.Print($"[LAYOUTDUMP] ===== END {tag} =====");
    }

    /// <summary>
    /// Waits <paramref name="framesToWait"/> process frames (for layout to settle) then dumps.
    /// Use from a scene's <c>_Ready</c> so the dump reflects the final built layout.
    /// </summary>
    public static async void DumpDeferred(Node root, string tag, int framesToWait = 3)
    {
        if (!Enabled || root is null) return;
        SceneTree? tree = root.GetTree();
        if (tree is not null)
            for (int i = 0; i < framesToWait; i++)
                await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Dump(root, tag);
    }

    private static void Walk(Node node, Vector2 canvasOffset, string tag, int depth)
    {
        Vector2 childOffset = canvasOffset;

        if (node is Control c)
        {
            Vector2 topLeft = canvasOffset + c.Position;
            childOffset = topLeft; // children are positioned relative to this control
            string indent = new string(' ', depth * 2);
            GD.Print(
                $"[LAYOUTDUMP] {tag} | {indent}{node.Name} ({node.GetType().Name}) " +
                $"canvas=({topLeft.X:0},{topLeft.Y:0},{c.Size.X:0},{c.Size.Y:0}) " +
                $"vis={(c.Visible ? 1 : 0)} visTree={(c.IsVisibleInTree() ? 1 : 0)}");
        }

        foreach (Node child in node.GetChildren())
            Walk(child, childOffset, tag, depth + 1);
    }
}