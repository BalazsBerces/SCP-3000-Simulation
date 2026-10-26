using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private void RecordChronicle(DomainEvent domainEvent)
    {
        string story;
        switch (domainEvent)
        {
            case VillageFounded founded:
                var name = Villager.NameOf(founded.Village.VillagerIds[0]);
                var home = $"Village {founded.Village.Id}";
                var place = ChroniclePlace(founded.Village.Position);
                var reason = founded.Forced ? "before their strength ran out" : "with food and water within reach";
                story = (_chronicle.Count % 3) switch
                {
                    0 => $"{name} and Band {founded.BandId} founded {home} in {place}, {reason}.",
                    1 => $"Band {founded.BandId} made a home in {place}: {name} called it {home}, {reason}.",
                    _ => $"In {place}, {name}'s Band {founded.BandId} put down roots as {home}, {reason}.",
                };
                break;
            case GearMade { First: true } made:
                story = $"{Villager.NameOf(made.VillagerId)} made the first Gear for Village {made.VillageId}: {Recipes.Basic.Single(r => r.Gear == made.Gear.Kind).Name}. The community learned by doing.";
                break;
            case VillagerDied died:
                var person = Villager.NameOf(died.Body.VillagerId);
                var village = _villages.FirstOrDefault(v => v.VillagerIds.Contains(died.Body.VillagerId));
                var community = village is null ? "" : $" of Village {village.Id}";
                var location = ChroniclePlace(died.Body.Position);
                var cause = died.Body.Cause == DeathCause.Thirst ? "thirst" : "starvation";
                story = (_chronicle.Count % 3) switch
                {
                    0 => $"{person}{community} died of {cause} in {location}. The aisles fell quiet.",
                    1 => $"In {location}, {cause} claimed {person}{community}. One fewer voice answered.",
                    _ => $"There was no relief for {person}{community} in {location}; {cause} took its toll.",
                };
                break;
            default:
                return;
        }
        _chronicle.Add($"Store day {domainEvent.At.Day}, {domainEvent.At.TimeOfDay:hh\\:mm}: {story}");
    }

    private string ChroniclePlace(Vector2 position) =>
        $"Sector {_sectors[Generation.PillarGenerator.SectorIndexAt(_space, _config.PillarLayout, position)].Name}";
}
