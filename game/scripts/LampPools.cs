using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The data behind the Lamp pools (lamp_pools.gdshaderinc): every display Lamp's bulb, range and
/// pool energy, and a grid over the wrapping Store listing the Lamps whose light reaches each cell.
/// Shared with all opaque materials as global shader parameters. A Lamp's pool energy is the
/// share of its light that its real light (if any) does not give, so the two always add up.
/// </summary>
public sealed class LampPools
{
    /// <summary>Grid cell size in metres; small enough that a cell lists few Lamps.</summary>
    public const float CellSize = 4f;

    private const int LampsPerRow = 1024;
    private const int IndicesPerRow = 4096;

    private float[] _energy = [];
    private float[] _poolEnergy = [];
    private byte[] _poolEnergyBytes = [];
    private Image? _poolEnergyImage;
    private ImageTexture? _poolEnergyTexture;
    private readonly List<int> _shared = [];
    private float[] _levels = [];
    private byte[] _levelBytes = [];
    private Image? _levelImage;
    private ImageTexture? _levelTexture;

    /// <summary>Most Lamps listed in one grid cell: the most Lamps any pool pixel adds up.</summary>
    public int MostLampsPerCell { get; private set; }

    /// <summary>Lists every Lamp, numbered from 0, with its whole energy in its pool.</summary>
    public void Build(StoreSpace space, IReadOnlyList<(Vector3 Bulb, float Range, float Energy)> lamps)
    {
        var across = (int)Mathf.Ceil(space.Size / CellSize);
        var cells = new List<int>[across * across];
        var bulbs = new float[Rows(lamps.Count, LampsPerRow) * LampsPerRow * 4];
        _energy = new float[lamps.Count];
        for (var i = 0; i < lamps.Count; i++)
        {
            var (bulb, range, energy) = lamps[i];
            bulb = new Vector3(Mathf.PosMod(bulb.X, space.Size), bulb.Y, Mathf.PosMod(bulb.Z, space.Size));
            bulbs[i * 4] = bulb.X;
            bulbs[i * 4 + 1] = bulb.Y;
            bulbs[i * 4 + 2] = bulb.Z;
            bulbs[i * 4 + 3] = range;
            _energy[i] = energy;
            // Every cell the light's footprint touches, wrapping across the Seams.
            var (x0, x1) = ((int)Mathf.Floor((bulb.X - range) / CellSize), (int)Mathf.Floor((bulb.X + range) / CellSize));
            var (z0, z1) = ((int)Mathf.Floor((bulb.Z - range) / CellSize), (int)Mathf.Floor((bulb.Z + range) / CellSize));
            for (var z = z0; z <= z1; z++)
                for (var x = x0; x <= x1; x++)
                    (cells[Mathf.PosMod(z, across) * across + Mathf.PosMod(x, across)] ??= []).Add(i);
        }

        var entries = new float[across * across * 2];
        var indices = new List<float>();
        MostLampsPerCell = 0;
        for (var c = 0; c < cells.Length; c++)
        {
            var listed = cells[c];
            if (listed is null)
                continue;
            entries[c * 2] = indices.Count;
            entries[c * 2 + 1] = listed.Count;
            MostLampsPerCell = Math.Max(MostLampsPerCell, listed.Count);
            indices.AddRange(listed.Select(i => (float)i));
        }
        indices.AddRange(new float[Rows(indices.Count, IndicesPerRow) * IndicesPerRow - indices.Count]);

        _poolEnergy = (float[])_energy.Clone();
        Array.Resize(ref _poolEnergy, Rows(lamps.Count, LampsPerRow) * LampsPerRow);
        _poolEnergyBytes = new byte[_poolEnergy.Length * sizeof(float)];
        Buffer.BlockCopy(_poolEnergy, 0, _poolEnergyBytes, 0, _poolEnergyBytes.Length);
        _poolEnergyImage = Image.CreateFromData(LampsPerRow, _poolEnergy.Length / LampsPerRow, false, Image.Format.Rf, _poolEnergyBytes);
        _poolEnergyTexture = ImageTexture.CreateFromImage(_poolEnergyImage);
        _levels = new float[_poolEnergy.Length];
        _levelBytes = new byte[_levels.Length * sizeof(float)];
        _levelImage = Image.CreateFromData(LampsPerRow, _levels.Length / LampsPerRow, false, Image.Format.Rf, _levelBytes);
        _levelTexture = ImageTexture.CreateFromImage(_levelImage);

        Set("lamp_cells", Texture(across, across, Image.Format.Rgf, entries));
        Set("lamp_indices", Texture(IndicesPerRow, indices.Count / IndicesPerRow, Image.Format.Rf, indices.ToArray()));
        Set("lamp_bulbs", Texture(LampsPerRow, bulbs.Length / 4 / LampsPerRow, Image.Format.Rgbaf, bulbs));
        Set("lamp_pool_energy", _poolEnergyTexture);
        Set("lamp_levels", _levelTexture);
        Set("lamp_cell_size", CellSize);
        Set("lamp_store_size", lamps.Count > 0 ? space.Size : 0f);
    }

    /// <summary>The warm light colour shared with the real Lamp lights.</summary>
    public static void SetColor(Color color)
    {
        // Godot converts light colours to linear the same way.
        var linear = color.SrgbToLinear();
        Set("lamp_light_color", new Vector3(linear.R, linear.G, linear.B));
    }

    /// <summary>
    /// Sets how far each Lamp has come on (indexed by Lamp number) and gives the listed Lamps that
    /// share of their light as real lights; every other Lamp's light is all pool. A Lamp's pool is its
    /// energy times its level, less the real share, so real light, pool and shade glow agree for a
    /// partly lit Lamp. Uploads the pool energies, and the levels only when they changed.
    /// </summary>
    public void Update(float[] levels, bool levelsChanged, IEnumerable<(int Lamp, float RealShare)> shares)
    {
        if (_poolEnergyImage is null || _levelImage is null)
            return;
        if (levelsChanged)
        {
            Array.Copy(levels, _levels, _energy.Length);
            for (var i = 0; i < _energy.Length; i++)
                _poolEnergy[i] = _energy[i] * levels[i];
            _shared.Clear();
        }
        // Only the Lamps with a real light differ from their level's pool, so only they are redone.
        foreach (var lamp in _shared)
            _poolEnergy[lamp] = _energy[lamp] * levels[lamp];
        _shared.Clear();
        foreach (var (lamp, share) in shares)
        {
            _poolEnergy[lamp] = _energy[lamp] * levels[lamp] * (1f - share);
            _shared.Add(lamp);
        }
        Upload(_poolEnergy, _poolEnergyBytes, _poolEnergyImage, _poolEnergyTexture!);
        if (levelsChanged)
            Upload(_levels, _levelBytes, _levelImage, _levelTexture!);
    }

    private static void Upload(float[] values, byte[] bytes, Image image, ImageTexture texture)
    {
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        image.SetData(LampsPerRow, values.Length / LampsPerRow, false, Image.Format.Rf, bytes);
        texture.Update(image);
    }

    /// <summary>Tells the materials every Lamp's level while none is switching, so they skip the per-Lamp lookup; -1 while Lamps switch.</summary>
    public static void SetSteadyLevel(float level) => Set("lamp_level_steady", level);

    private static int Rows(int count, int perRow) => Math.Max(1, (count + perRow - 1) / perRow);

    private static ImageTexture Texture(int width, int height, Image.Format format, float[] data)
    {
        var bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, format, bytes));
    }

    private static void Set(string name, Variant value) => RenderingServer.GlobalShaderParameterSet(name, value);
}
