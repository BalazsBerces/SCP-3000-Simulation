using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private readonly Dictionary<(int Village, int Dwelling), Label3D> _dwellings = [];

    private static string DwellingText(Village village)
    {
        var personal = Dwelling.Sharing(village.Personality) == DwellingSharing.Personal;
        return $"Sleeping places: {village.CoveredSleepingPlaces}/{village.VillagerIds.Count} | {Dwelling.Sharing(village.Personality)}\n" +
            string.Join("\n", village.Dwellings.Select(d => $"{d.Kind} {d.Id}: " +
                (d.Complete ? $"{(personal ? 1 : d.SleepingPlaces)} places" :
                    $"{d.Slots.Count(s => s.FurnitureId is not null)}/{d.Slots.Count} pieces, {d.SetupHours:0.0}/{d.RequiredSetupHours:0.0} h fitting")));
    }

    private void ShowDwellings()
    {
        var world = Host.Snapshot;
        var seen = new HashSet<(int, int)>();
        foreach (var village in world.Villages)
            foreach (var d in village.Dwellings)
            {
                var key = (village.Id, d.Id);
                seen.Add(key);
                if (!_dwellings.TryGetValue(key, out var label))
                {
                    label = new Label3D { Name = $"Dwelling{village.Id}_{d.Id}", FontSize = 36, PixelSize = 0.012f,
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
                    AddChild(label); _dwellings.Add(key, label);
                }
                var near = world.Space.Delta(Rig.Focus.ToStore(), d.Position);
                label.Position = new Vector3(Rig.Focus.X + near.X, 3.2f, Rig.Focus.Z + near.Y);
                var sleepers = world.Villagers.Count(v => v.VillageId == village.Id && v.SleepingPlace?.DwellingId == d.Id &&
                    v.Action == VillagerAction.Resting && v.Destination is null);
                label.Text = $"{d.Kind} {d.Id} | " + (d.Complete ? $"{sleepers}/{(Dwelling.Sharing(village.Personality) == DwellingSharing.Personal ? 1 : d.SleepingPlaces)} sleeping" : "building");
            }
        foreach (var key in _dwellings.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _dwellings[key].QueueFree(); _dwellings.Remove(key);
        }
    }
}
