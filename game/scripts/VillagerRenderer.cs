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
    private Label _communities = null!;
    private readonly Dictionary<string, Label3D> _markers = [];

    public override void _Ready()
    {
        _mesh = FurnitureModels.Product("villager", ModelYaw, Height, TriangleBudget);
        var layer = new CanvasLayer();
        AddChild(layer);
        CreateVillagePanel(layer);
        _needsPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_needsPanel);
        _needsPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _needsPanel.OffsetLeft = 16;
        _needsPanel.OffsetTop = -180;
        _needsPanel.OffsetRight = 420;
        _needsPanel.OffsetBottom = -16;
        _needs = new Label();
        _needsPanel.AddChild(_needs);
        var communitiesPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(communitiesPanel);
        communitiesPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        communitiesPanel.OffsetLeft = -460;
        communitiesPanel.OffsetTop = -230;
        communitiesPanel.OffsetRight = -16;
        communitiesPanel.OffsetBottom = -16;
        _communities = new Label { Name = "Communities", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        communitiesPanel.AddChild(_communities);
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
            _needs.Text = $"Villager {followed.Id} | {followed.Role} | {followed.Action}\n" +
                $"Hunger: {n.Hunger:P0}   Thirst: {n.Thirst:P0}\n" +
                $"Rest: {n.Rest:P0}   Safety: {n.Safety:P0}\nMorale: {n.Morale:P0}\n" +
                $"Load: {followed.CarriedAmount + followed.CarriedMaterials}/{followed.CarryingCapacity}\n" +
                $"Hand: {GearText(followed.Hand)} | Bag: {GearText(followed.Bag)}\nArmour: {GearText(followed.Armour)}";
        }
        var space = Host.Snapshot.Space;
        var focus = Rig.Focus;
        ShowCommunities(space, focus);
        ShowWorkshops();
        ShowMessHalls();
        ShowVillagePanel(Host.Snapshot, delta);
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
            if (villager.Action == VillagerAction.Disassembling)
                figure.Rotation = new Vector3(-0.3f + 0.08f * MathF.Sin((float)Time.GetTicksMsec() / 140), figure.Rotation.Y, 0);
            else if (villager.Action == VillagerAction.DraggingFurniture)
                figure.Rotation = new Vector3(-0.2f, figure.Rotation.Y, 0);
            else if (villager.Action == VillagerAction.SharedMeal)
                figure.Rotation = new Vector3(-0.1f + 0.025f * MathF.Sin((float)Time.GetTicksMsec() / 350), figure.Rotation.Y, 0);
            ShowGear(figure, villager);
        }
        foreach (var gone in _figures.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _figures[gone].QueueFree();
            _figures.Remove(gone);
        }
    }

    private static string GearText(Gear? gear) => gear is null ? "none" : $"{gear.Kind} ({gear.Durability:F0}%)";

    private void ShowCommunities(StoreSpace space, Vector3 focus)
    {
        var world = Host.Snapshot;
        var lines = world.Bands.Select(b => $"Band {b.Id}: {b.VillagerIds.Count} Wanderers")
            .Concat(world.Villages.Select(v => $"Village {v.Id}: {v.VillagerIds.Count} Villagers | " +
                $"Stores: {string.Join(", ", v.Stores.Select(g => $"{g.Key} {g.Value}"))} | " +
                $"Materials: {string.Join(", ", v.Materials.Select(m => $"{m.Key} {m.Value}"))}"));
        _communities.Text = string.Join("\n", lines.Concat(world.Chronicle.TakeLast(3)));
        _communities.GetParent<PanelContainer>().Visible = _selectedVillage is null && world.Bands.Count + world.Villages.Count > 0;
        var markers = world.Bands.Select(b => ($"Band {b.Id}", b.Position))
            .Concat(world.Villages.SelectMany(v => v.Outposts.Where(o => !o.Abandoned)
                .Select(o => ($"Village {v.Id} Outpost {o.Id}", o.Position)))).ToArray();
        foreach (var (name, position) in markers)
        {
            if (!_markers.TryGetValue(name, out var marker))
            {
                marker = new Label3D { Text = name, FontSize = 48, PixelSize = name.StartsWith("Village") ? 0.006f : 0.02f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
                AddChild(marker);
                _markers[name] = marker;
            }
            var near = space.Delta(focus.ToStore(), position);
            marker.Position = new Vector3(focus.X + near.X, 3, focus.Z + near.Y);
        }
        foreach (var gone in _markers.Keys.Where(k => !markers.Any(m => m.Item1 == k)).ToArray())
        {
            _markers[gone].QueueFree();
            _markers.Remove(gone);
        }
    }

    private void OnWorldCreated(WorldSnapshot snapshot)
    {
        CloseVillagePanel();
        foreach (var node in _messHalls.Values) node.QueueFree();
        _messHalls.Clear();
        foreach (var button in _villageButtons.Values)
            button.QueueFree();
        _villageButtons.Clear();
        foreach (var figure in _figures.Values)
            figure.QueueFree();
        _figures.Clear();
        foreach (var marker in _markers.Values)
            marker.QueueFree();
        _markers.Clear();
    }
}
