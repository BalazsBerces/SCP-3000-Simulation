using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private void RecordChronicle(DomainEvent domainEvent)
    {
        string story;
        switch (domainEvent)
        {
            case WanderersJoined joined:
                var wanderer = Villager.NameOf(joined.VillagerIds[0]);
                var joinPlace = ChroniclePlace(_villages[joined.VillageId].Position);
                story = (_chronicle.Count % 3) switch
                {
                    0 => $"{wanderer} and Band {joined.BandId} joined Village {joined.VillageId} in {joinPlace}, bringing news of the Store.",
                    1 => $"In {joinPlace}, Band {joined.BandId} joined Village {joined.VillageId}; {wanderer} shared what the Wanderers knew.",
                    _ => $"Following {wanderer}, Band {joined.BandId} joined Village {joined.VillageId} in {joinPlace} and found a home among its people.",
                };
                break;
            // Lone shoppers and pairs arrive all the time; only a group large enough to found a Village is of note.
            case WanderersArrived arrived when arrived.Band.VillagerIds.Count >= FoundingGroup:
                var leader = Villager.NameOf(arrived.Band.VillagerIds[0]);
                var count = arrived.Band.VillagerIds.Count;
                var arrivalPlace = ChroniclePlace(arrived.Band.Position);
                story = (_chronicle.Count % 3) switch
                {
                    0 => $"{leader} arrived in {arrivalPlace} with Band {arrived.Band.Id}: {count} Wanderers lost in the Store.",
                    1 => $"In {arrivalPlace}, {count} Wanderers arrived as Band {arrived.Band.Id}, led by {leader}.",
                    _ => $"Band {arrived.Band.Id} arrived among the aisles of {arrivalPlace}; {leader} searched for a way forward with {count} Wanderers.",
                };
                break;
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
            case OutpostFounded foundedPost:
                story = $"Village {foundedPost.VillageId} sent {Villager.NameOf(foundedPost.Outpost.VillagerIds[0])} and companions to Outpost {foundedPost.Outpost.Id} in {ChroniclePlace(foundedPost.Outpost.Position)}, following the dwindling shelves outward.";
                break;
            case OutpostAbandoned abandonedPost:
                story = $"Village {abandonedPost.VillageId} abandoned Outpost {abandonedPost.OutpostId}; its forward barricade fell quiet.";
                break;
            case RecipeLearned learned:
                story = $"Village {learned.VillageId} discovered a Recipe through {learned.Source}: {Recipes.All.Single(r => r.Gear == learned.Gear).Name}.";
                break;
            case GearMade { First: true } made:
                var maker = Villager.NameOf(made.VillagerId);
                var recipe = Recipes.All.Single(r => r.Gear == made.Gear.Kind).Name;
                var workshopPlace = ChroniclePlace(made.Gear.Position);
                story = (_chronicle.Count % 3) switch
                {
                    0 => $"{maker} made the first Gear for Village {made.VillageId} in {workshopPlace}: {recipe}. The community learned by doing.",
                    1 => $"In {workshopPlace}, {maker} finished {recipe}, the first Gear for Village {made.VillageId}.",
                    _ => $"Village {made.VillageId} celebrated its first Gear in {workshopPlace}: {maker}'s {recipe}.",
                };
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
