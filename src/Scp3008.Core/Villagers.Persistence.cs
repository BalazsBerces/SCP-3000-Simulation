using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Villagers
{
    internal sealed record TargetState(Vector2 Position, StockedGoods? Goods, WaterPoint? Water, bool Rest, bool Haul, Gear? Gear);
    internal sealed record WalkerState(Villager Villager, Vector2[] Waypoints, double StarvingHours,
        double ThirstyHours, double NextDecision, TargetState? Target, Vector2[] Unreachable);
    internal sealed record State(int NextId, WalkerState[] Walkers, Body[] Bodies);

    internal State Save() => new(_nextId, _walkers.Select(w => new WalkerState(View(w), w.Waypoints.ToArray(),
        w.StarvingHours, w.ThirstyHours, w.NextDecision,
        w.Target is { } t ? new TargetState(t.Position, t.Goods, t.Water, t.Rest, t.Haul, t.Gear) : null,
        w.Unreachable.ToArray())).ToArray(), Bodies.ToArray());

    internal void Restore(State state)
    {
        var ids = state.Walkers.Select(w => w.Villager.Id).Concat(state.Bodies.Select(b => b.VillagerId)).ToArray();
        if (state.NextId < 0 || ids.Any(id => id < 0 || id >= state.NextId) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Invalid Villager ids.");
        _nextId = state.NextId;
        _walkers.Clear();
        Bodies.Clear();
        Bodies.AddRange(state.Bodies);
        foreach (var saved in state.Walkers)
        {
            var v = saved.Villager;
            static bool Finite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
            if (!Finite(v.Position) || v.Destination is { } destination && !Finite(destination) ||
                !float.IsFinite(v.Heading) || saved.Waypoints.Any(p => !Finite(p)) ||
                !Enum.IsDefined(v.Role) || !Enum.IsDefined(v.Action))
                throw new ArgumentException("Invalid Villager movement or Role.");
            v.Needs.Validate();
            foreach (var gear in new[] { v.Hand, v.Bag, v.Armour }.OfType<Gear>()) gear.Validate();
            if (v.Hand is { Slot: not GearSlot.Hand } || v.Bag is { Slot: not GearSlot.Bag } ||
                v.Armour is { Slot: not GearSlot.Armour } || v.CarriedAmount < 0 || v.CarriedMaterials < 0 ||
                (v.CarriedGoods is null) != (v.CarriedAmount == 0))
                throw new ArgumentException("Invalid Villager Gear or load.");
            var w = new Walker(v.Id, v.Position)
            {
                Destination = v.Destination, Heading = v.Heading, Needs = v.Needs, Action = v.Action,
                Crafting = v.Crafting, FurnitureTaskId = v.FurnitureTaskId, VillageId = v.VillageId, Role = v.Role, CarriedGoods = v.CarriedGoods,
                CarriedAmount = v.CarriedAmount, CarriedMaterials = v.CarriedMaterials,
                Hand = v.Hand, Bag = v.Bag, Armour = v.Armour,
                Waypoints = saved.Waypoints.ToList(), StarvingHours = saved.StarvingHours,
                ThirstyHours = saved.ThirstyHours, NextDecision = saved.NextDecision,
                Target = saved.Target is { } t ? new NeedTarget(t.Position, t.Goods, t.Water, t.Rest, t.Haul, t.Gear) : null,
            };
            foreach (var skill in v.Skills)
            {
                if (!Enum.IsDefined(skill.Key) || !double.IsFinite(skill.Value) || skill.Value < 0)
                    throw new ArgumentException("Invalid Villager skill.");
                w.Skills.Add(skill.Key, skill.Value);
            }
            w.ExploredGround.UnionWith(v.KnownMap.ExploredGround);
            foreach (var g in v.KnownMap.Goods) w.KnownGoods.Add((g.Goods.FurnitureId, g.Goods.Kind, g.Goods.LeftBehind), g);
            foreach (var water in v.KnownMap.Water) w.KnownWater.Add((water.FurnitureId, water.Position), water);
            foreach (var danger in v.KnownMap.Dangers) w.KnownDangers.Add(danger.StaffId, danger);
            foreach (var village in v.KnownMap.Villages) w.KnownVillages.Add(village.VillageId, village);
            w.Unreachable.UnionWith(saved.Unreachable);
            _walkers.Add(w);
        }
    }
}
