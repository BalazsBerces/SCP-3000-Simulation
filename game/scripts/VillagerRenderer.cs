using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws every Villager as a standing figure at the image of their position nearest the camera focus, facing the way
/// they walk. View only: it never touches the simulation.
/// </summary>
public partial class VillagerRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    /// <summary>How tall a Villager stands, in metres.</summary>
    [Export] public float Height { get; set; } = 1.75f;

    // Turns the model so its face looks along +Z; checked in a capture.
    private const float ModelYaw = 0f;

    // A Villager is seen from a few metres away and there are few of them: far more detail than a shelf product.
    private const int TriangleBudget = 4000;

    private readonly Dictionary<int, MeshInstance3D> _figures = [];
    private Mesh _mesh = null!;
    private PanelContainer _needsPanel = null!;
    private Label _needs = null!;

    public override void _Ready()
    {
        _mesh = FurnitureModels.Product("villager", ModelYaw, Height, TriangleBudget);
        var layer = new CanvasLayer();
        AddChild(layer);
        _needsPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_needsPanel);
        _needsPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _needsPanel.OffsetLeft = 16;
        _needsPanel.OffsetTop = -180;
        _needsPanel.OffsetRight = 420;
        _needsPanel.OffsetBottom = -16;
        _needs = new Label();
        _needsPanel.AddChild(_needs);
        Host.WorldCreated += OnWorldCreated;
    }

    public override void _ExitTree() => Host.WorldCreated -= OnWorldCreated;

    public override void _Process(double delta)
    {
        var followed = Host.Snapshot.Villagers.FirstOrDefault(v => v.Id == Rig.Following);
        _needsPanel.Visible = followed is not null;
        if (followed is not null)
        {
            var n = followed.Needs;
            _needs.Text = $"Villager {followed.Id} | {followed.Action}\n" +
                $"Hunger: {n.Hunger:P0}   Thirst: {n.Thirst:P0}\n" +
                $"Rest: {n.Rest:P0}   Safety: {n.Safety:P0}\nMorale: {n.Morale:P0}\n" +
                "Hunger / thirst rise as supplies run out.";
        }
        var space = Host.Snapshot.Space;
        var focus = Rig.Focus;
        var seen = new HashSet<int>();
        var bodies = Host.Snapshot.Bodies.ToDictionary(b => b.VillagerId);
        var figures = Host.InterpolatedVillagers.Concat(bodies.Values.Select(b =>
            new Villager(b.VillagerId, b.Position, null, b.Heading))).Concat(Host.Snapshot.Staff.Select(s =>
            new Villager(-s.Id - 1, s.Position, s.Destination, s.Heading)));
        foreach (var villager in figures)
        {
            seen.Add(villager.Id);
            if (!_figures.TryGetValue(villager.Id, out var figure))
            {
                // Reuse the existing faceless mannequin in the sandbox until the Staff presentation ticket.
                figure = new MeshInstance3D
                {
                    Mesh = villager.Id < 0 ? FurnitureMeshes.For(FurnitureType.Mannequin) : _mesh,
                    Name = villager.Id < 0 ? $"Staff{-villager.Id - 1}" : $"Villager{villager.Id}",
                };
                AddChild(figure);
                _figures[villager.Id] = figure;
            }
            var near = space.Delta(focus.ToStore(), villager.Position);
            figure.Position = new Vector3(focus.X + near.X, bodies.ContainsKey(villager.Id) ? 0.25f : 0f, focus.Z + near.Y);
            // Heading runs from east towards south (+X towards +Z): face (cos, sin) in world X and Z.
            figure.Rotation = new Vector3(bodies.ContainsKey(villager.Id) ? MathF.PI / 2 : 0f,
                Mathf.Atan2(MathF.Cos(villager.Heading), MathF.Sin(villager.Heading)), 0f);
        }
        foreach (var gone in _figures.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _figures[gone].QueueFree();
            _figures.Remove(gone);
        }
    }

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        foreach (var figure in _figures.Values)
            figure.QueueFree();
        _figures.Clear();
    }
}
