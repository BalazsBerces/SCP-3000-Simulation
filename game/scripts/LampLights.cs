using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Display Lamps stay lit independently of the fluorescent Store lights. The Lamps nearest the
/// camera within the render budget get real lights; every Lamp's remaining light is a pool in the
/// opaque materials (<see cref="LampPools"/>), shaded the same way, so the two blend without a
/// visible boundary. All visible Lamps also have a soft halo. View only; snapshots are never changed.
/// </summary>
public partial class LampLights : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;
    [Export] public FogBankRenderer FogBanks { get; set; } = null!;
    [Export] public StoreVisibility Visibility { get; set; } = null!;

    /// <summary>Most Lamps lit by real lights at once (ADR 0004); the rest are pools only. Read once, when the lights are made.</summary>
    [Export] public int MaxLights { get; set; } = 256;

    /// <summary>Most lights casting shadows at once: the nearest of the lit Lamps. Read once, when the lights are made.</summary>
    [Export] public int ShadowBudget { get; set; } = 4;

    /// <summary>Distance from the camera within which real lights are considered.</summary>
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
    private readonly List<Candidate> _visible = [];
    private readonly List<Vector3> _bulbs = [];
    private readonly LampPools _pools = new();
    private LampHalos _halos = null!;
    private (Transform3D Transform, float Fov, float Near, float Far, bool Overview, Vector2 Viewport, float FurnitureDistance)? _lastView;
    private float _lastCutoff;
    public int VisibleLamps => _visible.Count;

    public LampLights() => _byShadowRank = (a, b) => ShadowRank(a).CompareTo(ShadowRank(b));

    /// <summary>Lamps lit by a real light this frame.</summary>
    public int ActiveLights { get; private set; }

    /// <summary>Lights casting shadows this frame; never more than <see cref="ShadowBudget"/>.</summary>
    public int ShadowedLights { get; private set; }

    private readonly record struct Lamp(int Id, int Index, Vector3 Bulb, float Range, float Energy);

    private readonly record struct Candidate(Lamp Lamp, Vector3 Position, float Distance);

    public override void _Ready()
    {
        // Dev captures compare real lights with pools by turning the real lights off.
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--lamp-lights="))
                MaxLights = int.Parse(arg["--lamp-lights=".Length..]);
            else if (arg.StartsWith("--lamp-shadows="))
                ShadowBudget = int.Parse(arg["--lamp-shadows=".Length..]);
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
                // The selected lights illuminate surfaces only. Camera-dependent selection must
                // never change the fog's illumination; source halos exist for every visible Lamp.
                LightVolumetricFogEnergy = 0f,
                ShadowEnabled = i < shadowed,
                OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid,
                ShadowBias = 0.05f,
                Visible = false,
            };
            AddChild(_lights[i]);
        }
        FurnitureMeshes.SetLampGlow(1f);
        LampPools.SetColor(Warm);
        _halos = new LampHalos();
        AddChild(_halos);
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    public override void _Process(double delta)
    {
        // The camera that renders: the Observer camera, or a dev camera such as the eye-level one.
        var camera = GetViewport().GetCamera3D() ?? Rig.Camera;
        _halos.ApplyFog(material =>
        {
            Atmosphere.ApplyLampFog(material);
            FogBanks.ApplyLampFog(material);
        });
        var view = (camera.GlobalTransform, camera.Fov, camera.Near, camera.Far,
            StoreOverview.Enabled, GetViewport().GetVisibleRect().Size, Visibility.FurnitureDistance);
        // Display Lamps are static between world rebuilds. A stationary view needs no ranking,
        // uploads or light changes, even while the Store clock and Weather continue to advance.
        if (_lastView == view)
            return;
        _lastView = view;
        var planes = camera.GetFrustum();
        for (var i = 0; i < _frustum.Length; i++)
            _frustum[i] = planes[i];
        GatherNearest(camera.GlobalPosition);

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
        _lastCutoff = cutoff;
        ShowLit(cutoff);
        _pools.ShareWithRealLights(_lit.Select(c => (c.Lamp.Index, RealWeight(c.Distance, cutoff))));
        _bulbs.Clear();
        _bulbs.AddRange(_visible.Select(c => c.Position));
        _halos.Show(_bulbs);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"{ActiveLights} lamp lights ({ShadowedLights} shadowed, budget {_lights.Length} lights, {_shadowSlots.Length} shadows; " +
        $"shadow opacity {string.Join("/", _shadowSlots.Select((id, slot) => id >= 0 ? $"{_lights[slot].ShadowOpacity:0.00}" : "-"))}), " +
        $"{VisibleLamps} visible Lamps with halos, {_lampsByChunk.Values.Sum(l => l.Length)} Lamps in the Store " +
        $"(at most {_pools.MostLampsPerCell} per pool cell)";

    /// <summary>Dev evidence: identifies one stationary Lamp's contribution during camera motion.</summary>
    public string DescribeLamp(int id)
    {
        var index = _visible.FindIndex(c => c.Lamp.Id == id);
        if (index < 0)
            return $"Lamp {id} outside view";
        var candidate = _visible[index];
        var lit = _lit.FindIndex(c => c.Lamp.Id == id);
        var real = lit >= 0 ? RealWeight(_lit[lit].Distance, _lastCutoff) : 0f;
        var slot = Array.IndexOf(_shadowSlots, id);
        var shadow = slot >= 0 ? $", shadow opacity {_lights[slot].ShadowOpacity:0.000}" : "";
        return $"Lamp {id} at {candidate.Position}, {candidate.Distance:0.0} m from the camera: real {real:0.000}, pool {1f - real:0.000}{shadow}";
    }

    /// <summary>
    /// Collects every Lamp whose light reaches the view (for halos) and ranks them by distance from
    /// the camera, so the real-light boundary runs in depth, where fog and distance hide it.
    /// </summary>
    private void GatherNearest(Vector3 eye)
    {
        _nearest.Clear();
        _visible.Clear();
        var keep = _lights.Length + 1;

        foreach (var image in _space.ChunkImagesAround(Rig.Focus.ToStore(), StoreOverview.Enabled ? _space.Size * 2f :
                     Visibility.PlacementReach(Rig, _space.ChunkSize) + _space.ChunkSize))
        {
            if (!_lampsByChunk.TryGetValue(image.Chunk, out var lamps))
                continue;
            if (StoreOverview.Enabled && image.Offset != System.Numerics.Vector2.Zero)
                continue;
            var offset = image.Offset.ToWorld();
            var centre = (_space.ChunkOrigin(image.Chunk) + image.Offset + new System.Numerics.Vector2(_space.ChunkSize / 2f)).ToWorld();
            // Furniture's MultiMesh bounds can be off the geometric chunk centre. Include a
            // conservative diagonal of padding beyond the shared visibility range so no drawn Lamp is missed.
            if (!StoreOverview.Enabled && centre.DistanceTo(eye) > Visibility.LampDistance(_space.ChunkSize))
                continue;
            foreach (var lamp in lamps)
            {
                var position = lamp.Bulb + offset;
                if (!Reaches(position, lamp.Range))
                    continue;
                var distance = position.DistanceTo(eye);
                _visible.Add(new Candidate(lamp, position, distance));
                if (distance >= MaxDistance)
                    continue;
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
    private void ShowLit(float cutoff)
    {
        var count = _lit.Count;
        var shadowCutoff = ShadowCutoff();
        var next = _shadowSlots.Length;
        for (var i = 0; i < count; i++)
        {
            var candidate = _lit[i];
            var shadowSlot = Array.IndexOf(_shadowSlots, candidate.Lamp.Id);
            var light = _lights[shadowSlot >= 0 ? shadowSlot : next++];
            light.Position = candidate.Position;
            light.OmniRange = candidate.Lamp.Range;
            // Lights near the edge of the budget fade out, and their pools take over the rest.
            light.LightEnergy = candidate.Lamp.Energy * RealWeight(candidate.Distance, cutoff);
            if (shadowSlot >= 0)
                light.ShadowOpacity = ShadowWeight(candidate.Distance, shadowCutoff);
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

    /// <summary>The distance of the nearest lit Lamp without a shadow, or the reach if every lit Lamp has one.</summary>
    private float ShadowCutoff()
    {
        var cutoff = MaxDistance;
        foreach (var candidate in _lit)
            if (Array.IndexOf(_shadowSlots, candidate.Lamp.Id) < 0)
                cutoff = Math.Min(cutoff, candidate.Distance);
        return cutoff;
    }

    /// <summary>
    /// Shadows fade out towards the nearest Lamp without one, and are gone before a Lamp could take
    /// over the shadow (at the hysteresis distance), so no shadow ever appears or vanishes at once.
    /// </summary>
    private float ShadowWeight(float distance, float cutoff) =>
        1f - Mathf.SmoothStep(cutoff * 0.5f, cutoff * ShadowHysteresis, distance);

    /// <summary>Complementary spatial blend; a Lamp leaving the real-light budget has zero real energy.</summary>
    private static float RealWeight(float distance, float cutoff) =>
        cutoff > 0.001f ? 1f - Mathf.SmoothStep(cutoff * 0.8f, cutoff, distance) : 0f;

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
        _lastView = null;
        _lampsByChunk.Clear();
        Array.Fill(_shadowSlots, -1);
        var lamps = snapshot.Furniture.Where(p => FurnitureCatalog.Spec(p.Type).IsLamp).Select(ToLamp).ToList();
        foreach (var chunk in lamps.GroupBy(l => _space.ChunkOf(l.Bulb.ToStore())))
            _lampsByChunk[chunk.Key] = chunk.ToArray();
        _pools.Build(_space, lamps.Select(l => (l.Bulb, l.Range, l.Energy)).ToList());
    }

    private Lamp ToLamp(FurniturePiece piece, int index)
    {
        var spec = FurnitureCatalog.Spec(piece.Type);
        var offset = FurnitureMeshes.BulbOffset(piece.Type);
        var horizontal = new System.Numerics.Vector2(offset.X * MathF.Cos(piece.Rotation), offset.X * MathF.Sin(piece.Rotation));
        var bulb = _space.Wrap(piece.Position + horizontal).ToWorld(piece.Elevation + offset.Y);
        // Bigger Lamps carry brighter bulbs.
        return new Lamp(piece.Id, index, bulb, spec.LightRadius, 0.35f * spec.LightRadius);
    }
}
