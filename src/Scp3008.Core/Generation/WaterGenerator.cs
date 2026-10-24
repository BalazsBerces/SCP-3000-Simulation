namespace Scp3008.Core.Generation;

/// <summary>
/// Lists the Store's Water points: each Restroom's washbasins, the fountain court, the Camping ground's lake and every showroom
/// sink, of which the seed picks the few that run.
/// </summary>
internal static class WaterGenerator
{
    private const int WaterStream = 9;

    public static IReadOnlyList<WaterPoint> Generate(int seed, WaterConfig config, IReadOnlyList<FurniturePiece> pieces,
        IReadOnlyList<Display> displays, IReadOnlyList<PointOfInterest> pointsOfInterest, IReadOnlyList<Landmark> landmarks)
    {
        var points = new List<WaterPoint>();
        var sinks = new List<FurniturePiece>();
        var kitchenDone = new HashSet<int>();
        foreach (var piece in pieces)
        {
            if (piece is { DisplayId: >= 0 })
            {
                var theme = displays[piece.DisplayId].Theme;
                if (theme == Theme.Bathroom && piece.Type == FurnitureType.VanityUnit)
                    sinks.Add(piece);
                // The first cabinet of a kitchen is its sink unit; the tall pantry (variant 2) cannot be one.
                else if (theme == Theme.Kitchen && piece is { Type: FurnitureType.KitchenCabinet, Variant: not 2 } && kitchenDone.Add(piece.DisplayId))
                    sinks.Add(piece);
            }
            else if (piece is { Type: FurnitureType.VanityUnit, PointOfInterestId: >= 0 } &&
                     pointsOfInterest[piece.PointOfInterestId].Type == PointOfInterestType.StoreRestroom)
                points.Add(new WaterPoint(WaterPointKind.RestroomWashbasin, piece.Position, true, piece.Id));
        }
        foreach (var landmark in landmarks)
        {
            if (landmark.Type == LandmarkType.FountainCourt)
                points.Add(new WaterPoint(WaterPointKind.FountainCourt, landmark.Centre, true, -1));
            else if (landmark is { Type: LandmarkType.CampingGround, Lake: { } lake })
                points.Add(new WaterPoint(WaterPointKind.CampingLake, lake, true, -1));
        }

        // A partial shuffle of the sinks picks exactly the share that runs, the same ones whenever the seed is.
        var running = sinks.Count == 0 || config.RunningTapShare <= 0f ? 0 : Math.Min(sinks.Count, (int)Math.Ceiling(sinks.Count * config.RunningTapShare));
        var random = SeededRandom.For(seed, WaterStream);
        for (var i = 0; i < running; i++)
        {
            var j = i + random.Next(sinks.Count - i);
            (sinks[i], sinks[j]) = (sinks[j], sinks[i]);
        }
        for (var i = 0; i < sinks.Count; i++)
            points.Add(new WaterPoint(WaterPointKind.ShowroomTap, sinks[i].Position, i < running, sinks[i].Id));
        return points.AsReadOnly();
    }
}
