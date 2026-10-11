using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Real lights for the Lamps nearest the camera, within the render budget (ADR 0004). Every frame
/// the <see cref="MaxLights"/> Lamps nearest the camera whose light reaches into view each get an
/// unshadowed clustered light, and only the <see cref="ShadowBudget"/> nearest of those cast
/// shadows; Lamps farther away only glow. Lamps on display run on the Store's power, so the lights
/// and the glow follow the Store light level, flicker included, and are out at Closing time.
/// Rebuilt whenever a new world is created. View only: it never touches the simulation.
/// </summary>
public partial class LampLights : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;

    /// <summary>Most Lamps lit by real lights at once; the rest only glow. Read once, when the lights are made.</summary>
    [Export] public int MaxLights { get; set; } = 48;

    /// <summary>Most lights casting shadows at once: the nearest of the lit Lamps. Read once, when the lights are made.</summary>
    [Export] public int ShadowBudget { get; set; } = 4;

    /// <summary>Lamps farther than this from the camera get no real light.</summary>
    [Export] public float MaxDistance { get; set; } = 320f;

    /// <summary>Shadow-casting Lamps keep their shadows unless another Lamp is this much nearer (no flapping).</summary>
    [Export] public float ShadowHysteresis { get; set; } = 0.85f;

    private static readonly Color Warm = new(1f, 0.78f, 0.52f);

    private readonly Dictionary<ChunkCoord, Lamp[]> _lampsByChunk = new();
    // The nearest candidates, farthest first out; holds one more than there are lights, for the fade cutoff.
    private readonly PriorityQueue<Candidate, float> _nearest = new();
    private readonly List<Candidate> _lit = [];
    private readonly List<Candidate> _shadowCasters = [];
    private readonly Plane[] _frustum = new Plane[6];
    private OmniLight3D[] _lights = [];
    // The Lamp id each shadowed light is on, so a shadow-casting Lamp keeps its light (and shadow map).
    private int[] _shadowSlots = [];
    private readonly Comparison<Candidate> _byShadowRank;
    private StoreSpace _space = StoreSpace.Standard;
    private float _glow = -1f;

    public LampLights() => _byShadowRank = (a, b) => ShadowRank(a).CompareTo(ShadowRank(b));

    /// <summary>Lamps lit by a real light this frame.</summary>
    public int ActiveLights { get; private set; }

    /// <summary>Lights casting shadows this frame; never more than <see cref="ShadowBudget"/>.</summary>
    public int ShadowedLights { get; private set; }

    private readonly record struct Lamp(int Id, Vector3 Bulb, float Range, float Energy);

    private readonly record struct Candidate(Lamp Lamp, Vector3 Position, float Distance);

    public override void _Ready()
    {
        var shadowed = Math.Clamp(ShadowBudget, 0, MaxLights);
        _lights = new OmniLight3D[MaxLights];
        _shadowSlots = Enumerable.Repeat(-1, shadowed).ToArray();
        for (var i = 0; i < MaxLights; i++)
        {
            // The first slots are the shadow budget; the rest never cast shadows.
            _lights[i] = new OmniLight3D
            {
                Name = i < shadowed ? $"ShadowedLamp{i}" : $"Lamp{i}",
                LightColor = Warm,
                ShadowEnabled = i < shadowed,
                OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid,
                ShadowBias = 0.05f,
                Visible = false,
            };
            AddChild(_lights[i]);
        }
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    public override void _Process(double delta)
    {
        var level = Atmosphere.ShownLightLevel;
        if (level != _glow)
            FurnitureMeshes.SetLampGlow(_glow = level);
        if (level <= 0.001f)
        {
            ShowLit(0, 0f, 0f);
            return;
        }

        // The camera that renders: the Observer camera, or a dev camera such as the eye-level one.
        var camera = GetViewport().GetCamera3D() ?? Rig.Camera;
        var planes = camera.GetFrustum();
        for (var i = 0; i < _frustum.Length; i++)
            _frustum[i] = planes[i];
        var eye = camera.GlobalPosition;
        GatherNearest(eye);

        _lit.Clear();
        while (_nearest.Count > 0)
            _lit.Add(_nearest.Dequeue());
        _lit.Reverse();
        // The fade cutoff is the nearest Lamp that missed the budget, or the reach if none did.
        var cutoff = MaxDistance;
        if (_lit.Count > _lights.Length)
        {
            cutoff = _lit[^1].Distance;
            _lit.RemoveAt(_lit.Count - 1);
        }
        ChooseShadowCasters();
        ShowLit(_lit.Count, level, cutoff);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"{ActiveLights} lamp lights ({ShadowedLights} shadowed, budget {_shadowSlots.Length}), " +
        $"{_lampsByChunk.Values.Sum(l => l.Length)} Lamps in the Store";

    /// <summary>Keeps the Lamps nearest the eye whose light reaches into view: one more than there are lights.</summary>
    private void GatherNearest(Vector3 eye)
    {
        _nearest.Clear();
        var keep = _lights.Length + 1;
        var reach = MaxDistance * MaxDistance;
        foreach (var image in _space.ChunkImagesAround(eye.ToStore(), MaxDistance))
        {
            if (!_lampsByChunk.TryGetValue(image.Chunk, out var lamps))
                continue;
            var offset = image.Offset.ToWorld();
            foreach (var lamp in lamps)
            {
                var position = lamp.Bulb + offset;
                var distanceSquared = position.DistanceSquaredTo(eye);
                if (distanceSquared > reach || !Reaches(position, lamp.Range))
                    continue;
                var distance = Mathf.Sqrt(distanceSquared);
                // Priorities are negated distances, so the farthest kept Lamp is dequeued first.
                if (_nearest.Count < keep)
                    _nearest.Enqueue(new Candidate(lamp, position, distance), -distance);
                else if (_nearest.TryPeek(out _, out var farthest) && distance < -farthest)
                    _nearest.EnqueueDequeue(new Candidate(lamp, position, distance), -distance);
            }
        }
    }

    /// <summary>
    /// Gives the shadow budget to the nearest lit Lamps, with a bonus for those already casting
    /// shadows (so two Lamps at about the same distance do not swap shadows every frame), and
    /// keeps every Lamp that stays shadowed on the light it already had.
    /// </summary>
    private void ChooseShadowCasters()
    {
        _shadowCasters.Clear();
        _shadowCasters.AddRange(_lit);
        _shadowCasters.Sort(_byShadowRank);
        if (_shadowCasters.Count > _shadowSlots.Length)
            _shadowCasters.RemoveRange(_shadowSlots.Length, _shadowCasters.Count - _shadowSlots.Length);

        for (var slot = 0; slot < _shadowSlots.Length; slot++)
            if (!_shadowCasters.Exists(c => c.Lamp.Id == _shadowSlots[slot]))
                _shadowSlots[slot] = -1;
        foreach (var caster in _shadowCasters)
            if (Array.IndexOf(_shadowSlots, caster.Lamp.Id) < 0)
                _shadowSlots[Array.IndexOf(_shadowSlots, -1)] = caster.Lamp.Id;
    }

    private float ShadowRank(Candidate candidate) =>
        Array.IndexOf(_shadowSlots, candidate.Lamp.Id) >= 0 ? candidate.Distance * ShadowHysteresis : candidate.Distance;

    /// <summary>Puts the lit Lamps on lights: shadow casters on their shadowed slots, the rest on the others.</summary>
    private void ShowLit(int count, float level, float cutoff)
    {
        var next = _shadowSlots.Length;
        for (var i = 0; i < count; i++)
        {
            var candidate = _lit[i];
            var shadowSlot = Array.IndexOf(_shadowSlots, candidate.Lamp.Id);
            var light = _lights[shadowSlot >= 0 ? shadowSlot : next++];
            light.Position = candidate.Position;
            light.OmniRange = candidate.Lamp.Range;
            // Lights near the edge of the budget fade out, so Lamps slip in and out of it smoothly.
            light.LightEnergy = candidate.Lamp.Energy * level * Mathf.SmoothStep(cutoff, cutoff * 0.8f, candidate.Distance);
            light.Visible = true;
        }
        for (var slot = 0; slot < _lights.Length; slot++)
        {
            var used = slot < _shadowSlots.Length ? count > 0 && _shadowSlots[slot] >= 0 : slot < next;
            if (!used)
                _lights[slot].Visible = false;
        }
        if (count == 0)
            Array.Fill(_shadowSlots, -1);
        ActiveLights = count;
        ShadowedLights = count == 0 ? 0 : _shadowSlots.Count(id => id >= 0);
    }

    /// <summary>Whether a light's sphere reaches into the view frustum (Godot frustum planes face outwards).</summary>
    private bool Reaches(Vector3 centre, float radius)
    {
        foreach (var plane in _frustum)
            if (plane.DistanceTo(centre) > radius)
                return false;
        return true;
    }

    private void Rebuild(WorldSnapshot snapshot)
    {
        _space = snapshot.Space;
        _lampsByChunk.Clear();
        Array.Fill(_shadowSlots, -1);
        foreach (var chunk in snapshot.Furniture
                     .Where(p => FurnitureCatalog.Spec(p.Type).IsLamp)
                     .GroupBy(p => _space.ChunkOf(p.Position)))
            _lampsByChunk[chunk.Key] = chunk.Select(ToLamp).ToArray();
    }

    private Lamp ToLamp(FurniturePiece piece)
    {
        var spec = FurnitureCatalog.Spec(piece.Type);
        var bulb = _space.Wrap(piece.Position).ToWorld(piece.Elevation + FurnitureMeshes.BulbHeight(piece.Type));
        // Bigger Lamps carry brighter bulbs.
        return new Lamp(piece.Id, bulb, spec.LightRadius, 0.35f * spec.LightRadius);
    }
}
