using System.Numerics;

namespace Scp3008.Core;

/// <summary>Paint used for a Pillar's broad navigation bands.</summary>
public enum PillarBand { Ochre, Red, Blue, Green, White }

/// <summary>A permanent, visible damage marking on the concrete.</summary>
public enum PillarDamage { VerticalCrack, DiagonalCrack, ChippedCorner }

/// <summary>Painted number, colour bands and damage that identify a Pillar. Numbers are unique within a Store.</summary>
public sealed record PillarMarking(string Number, PillarBand Band, PillarDamage Damage);

/// <summary>
/// A static concrete Landmark, separate from Furniture. Its square footprint is centred on
/// Position; Height and Width are in metres. Pillars never give light or power.
/// </summary>
public sealed record Pillar(int Id, Vector2 Position, float Width, float Height, PillarMarking Marking);
