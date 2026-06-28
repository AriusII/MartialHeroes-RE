using Godot;

namespace MartialHeroes.Explorer.Viewer;

public static class ViewerGizmos
{
    public static Node3D BuildAxisGizmo(float length = 1f)
    {
        var root = new Node3D { Name = "AxisGizmo" };

        root.AddChild(BuildArm(length, new Color(1f, 0.15f, 0.15f), new Vector3(length * 0.5f, 0f, 0f),
            new Vector3(length, length * 0.04f, length * 0.04f), "ArmX"));
        root.AddChild(BuildArm(length, new Color(0.15f, 1f, 0.15f), new Vector3(0f, length * 0.5f, 0f),
            new Vector3(length * 0.04f, length, length * 0.04f), "ArmY"));
        root.AddChild(BuildArm(length, new Color(0.15f, 0.4f, 1f), new Vector3(0f, 0f, length * 0.5f),
            new Vector3(length * 0.04f, length * 0.04f, length), "ArmZ"));

        root.AddChild(BuildLabel("X", new Color(1f, 0.15f, 0.15f), new Vector3(length * 1.1f, 0f, 0f)));
        root.AddChild(BuildLabel("Y", new Color(0.15f, 1f, 0.15f), new Vector3(0f, length * 1.1f, 0f)));
        root.AddChild(BuildLabel("Z", new Color(0.15f, 0.4f, 1f), new Vector3(0f, 0f, length * 1.1f)));

        return root;
    }

    private static MeshInstance3D BuildArm(float length, Color color, Vector3 position, Vector3 size, string name)
    {
        var box = new BoxMesh { Size = size };

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color
        };
        box.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Name = name,
            Mesh = box,
            Position = position
        };
    }

    private static Label3D BuildLabel(string text, Color color, Vector3 position)
    {
        return new Label3D
        {
            Name = $"Label{text}",
            Text = text,
            Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = position,
            FontSize = 64,
            PixelSize = 0.005f
        };
    }
}