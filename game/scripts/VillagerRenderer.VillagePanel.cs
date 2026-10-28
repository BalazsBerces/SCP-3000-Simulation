using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private readonly Dictionary<int, Button> _villageButtons = [];
    private CanvasLayer _villageLayer = null!;
    private PanelContainer _villagePanel = null!;
    private Label _villageInfo = null!;
    private int? _selectedVillage;
    private double _villageRefresh;

    private void CreateVillagePanel(CanvasLayer layer)
    {
        _villageLayer = layer;
        _villagePanel = new PanelContainer { Name = "VillagePanel", Visible = false, ZIndex = 1 };
        layer.AddChild(_villagePanel);
        _villagePanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        _villagePanel.OffsetLeft = -440;
        _villagePanel.OffsetRight = -16;
        _villagePanel.OffsetTop = 232;
        _villagePanel.OffsetBottom = -16;
        var content = new VBoxContainer();
        _villagePanel.AddChild(content);
        var close = new Button { Name = "CloseVillage", Text = "Close Village panel (Esc)" };
        close.Pressed += CloseVillagePanel;
        content.AddChild(close);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.AddChild(scroll);
        _villageInfo = new Label
        {
            Name = "VillageInfo", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(_villageInfo);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_selectedVillage is not null && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CloseVillagePanel();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var from = Rig.Camera.ProjectRayOrigin(click.Position);
            var direction = Rig.Camera.ProjectRayNormal(click.Position);
            if (direction.Y >= 0) return;
            var hit = (from + direction * (-from.Y / direction.Y)).ToStore();
            var world = Host.Snapshot;
            var village = world.Villages.MinBy(v => world.Space.Distance(v.Position, hit));
            if (village is not null && world.Space.Distance(village.Position, hit) <= 8)
            {
                SelectVillage(village.Id);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void SelectVillage(int id)
    {
        _selectedVillage = id;
        _villageRefresh = 0.25;
        ShowVillagePanel(Host.Snapshot, 0);
    }

    private void CloseVillagePanel()
    {
        _selectedVillage = null;
        _villagePanel.Hide();
    }

    private void ShowVillagePanel(WorldSnapshot world, double delta)
    {
        foreach (var village in world.Villages)
        {
            if (!_villageButtons.TryGetValue(village.Id, out var button))
            {
                var id = village.Id;
                button = new Button { Name = $"InspectVillage{id}", Text = $"Village {id}",
                    TooltipText = "Inspect population, stores and history" };
                button.Pressed += () => SelectVillage(id);
                _villageLayer.AddChild(button);
                _villageButtons.Add(id, button);
            }
            var near = world.Space.Delta(Rig.Focus.ToStore(), village.Position);
            var position = new Vector3(Rig.Focus.X + near.X, 3, Rig.Focus.Z + near.Y);
            button.Visible = !Rig.Camera.IsPositionBehind(position);
            if (button.Visible)
                button.Position = Rig.Camera.UnprojectPosition(position) - button.Size / 2;
        }
        _villageRefresh += delta;
        if (_villageRefresh < 0.25 || _selectedVillage is not { } selected) return;
        _villageRefresh = 0;
        var current = world.Villages.FirstOrDefault(v => v.Id == selected);
        if (current is null)
        {
            CloseVillagePanel();
            return;
        }
        var members = world.Villagers.Where(v => v.VillageId == selected).ToArray();
        var materials = current.Materials.Count == 0 ? "Empty" :
            string.Join("\n", current.Materials.OrderBy(m => m.Key).Select(m => $"{m.Key}: {m.Value}"));
        var stores = current.Stores.Count == 0 ? "Empty" :
            string.Join("\n", current.Stores.OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Value}"));
        var workshops = string.Join(", ", current.Workshops.Select(w => $"{w.Kind}: {(w.Complete ? "built" : "planned")}"));
        var messHall = current.MessHall is not { } hall ? "Not planned" :
            $"{(hall.Complete ? "Built" : $"{hall.Slots.Count(s => s.FurnitureId is not null)}/4 pieces")} | Meals: {string.Join(", ", MessHall.MealHours.Select(h => $"{h:00}:00"))}\n" +
            $"Eating together: {members.Count(v => v.Action == VillagerAction.SharedMeal)}";
        var recipes = string.Join("\n", current.Recipes.Select(k => Recipes.All.Single(r => r.Gear == k).Name));
        var skills = string.Join("\n", members.Select(v => $"{v.Name}: " + (v.Skills.Count == 0 ? "Unpractised" :
            string.Join(", ", v.Skills.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value:0.0}")))));
        var gear = current.Gear.Count == 0 ? "Empty" : string.Join("\n", current.Gear.Select(g => $"{g.Kind}: {g.Durability:0.0}"));
        var outposts = current.Outposts.Count == 0 ? "None" : string.Join("\n", current.Outposts.Select(o =>
            $"Outpost {o.Id} | Sector {world.SectorAt(o.Position).Name} | {(o.Abandoned ? "abandoned" : $"{o.VillagerIds.Count} Villagers")} | " +
            $"{current.Walls[o.WallIndex].Slots.Count(s => s.FurnitureId is not null)}/{current.Walls[o.WallIndex].Slots.Count} barricade pieces"));
        var homeWalls = current.Walls.Where((_, i) => !current.Outposts.Any(o => o.WallIndex == i)).ToArray();
        var wall = homeWalls.Length == 0 ? "Not planned" : string.Join("; ", homeWalls.Select(w =>
            $"{w.Outline}: {w.Slots.Count(s => s.FurnitureId is not null)}/{w.Slots.Count} pieces"));
        // shortcut: history identifies Villages by their numbered names; use event references when names become editable.
        var history = world.Chronicle.Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, $@"\bVillage {selected}\b"));
        _villageInfo.Text = $"Village {selected} | Sector {world.SectorAt(current.Position).Name}\n\n" +
            $"Mess hall\n{messHall}\n\nPopulation: {members.Length}\n\nOutposts\n{outposts}\n\n" + string.Join("\n", members.Select(v => $"{v.Name} — {v.Role}")) +
            $"\n\nStores\n{stores}\n\nMaterials\n{materials}\n\nWorkshops\n{workshops}\n\nRecipes\n{recipes}\n\nSkills\n{skills}\n\nGear\n{gear}\n\nWall condition: {wall}\nBeliefs: —\nRelations: —\n\nHistory\n" +
            string.Join("\n\n", history);
        _villagePanel.Show();
    }
}
