using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// The Weather subsystem: Fog banks form at random places, thicken and spread out, drift with a
/// slowly changing wind across the wrapping Store, then thin out and dissipate, all over Store
/// hours. New banks form only while the banks at their full size would cover at most
/// <see cref="MaxCoverage"/> of the floor, so clear stretches always remain between them.
/// Driven by runtime randomness, never by the Store seed (ADR 0003).
/// </summary>
internal sealed class Weather
{
    /// <summary>The largest share of the Store floor that Fog banks may cover between them.</summary>
    public const float MaxCoverage = 0.45f;

    private const float MinRadius = 100f;
    private const float MaxRadius = 220f;
    private const float MinPeakDensity = 0.65f;

    // A forming bank starts at this share of its full size; a dissipating one spreads out by this much as it thins.
    private const float FormingStartSize = 0.45f;
    private const float DissipatingSpread = 0.15f;

    // New banks per Store hour while there is room for them; faster while few banks are not yet thinning out.
    private const double FormationRate = 0.6;
    private const double FormationRateWhenThin = 3.0;
    private const int FewBanks = 4;

    // Wind speed in metres per Store hour, how fast it turns and changes speed, and how often it picks a new aim.
    private const float MinWindSpeed = 20f;
    private const float MaxWindSpeed = 70f;
    private const float WindTurnDegreesPerHour = 12f;
    private const float WindSpeedChangePerHour = 10f;
    private const double WindShiftsPerHour = 0.25;
    private const float MaxWindShiftDegrees = 70f;

    private readonly StoreSpace _space;
    private readonly SeededRandom _random;
    private readonly List<Bank> _banks = [];
    private int _nextId = 1;
    private float _windHeading;
    private float _windSpeed;
    private float _targetHeading;
    private float _targetSpeed;
    private bool _windHeld;
    private bool _formation = true;

    public Weather(StoreSpace space, SeededRandom random)
    {
        _space = space;
        _random = random;
        _windHeading = _targetHeading = _random.Range(0f, MathF.Tau);
        _windSpeed = _targetSpeed = _random.Range(MinWindSpeed, MaxWindSpeed);
    }

    /// <summary>The wind's velocity in metres per Store hour (X east, Y south).</summary>
    public Vector2 Wind => new Vector2(MathF.Cos(_windHeading), MathF.Sin(_windHeading)) * _windSpeed;

    /// <summary>
    /// Lets the weather run for <paramref name="hours"/> Store hours with no events, so a new
    /// world opens with weather already under way rather than a fog-free Store.
    /// </summary>
    public void WarmUp(double hours)
    {
        const double step = 0.05;
        for (var t = 0.0; t < hours; t += step)
            Advance(step, default, null);
    }

    /// <summary>Advances the weather by <paramref name="hours"/> Store hours to <paramref name="now"/>; <paramref name="emit"/> receives Fog bank events.</summary>
    public void Advance(double hours, StoreClock now, Action<DomainEvent>? emit)
    {
        UpdateWind(hours);
        var drift = Wind * (float)hours;
        for (var i = _banks.Count - 1; i >= 0; i--)
        {
            var bank = _banks[i];
            bank.Age += hours;
            bank.Position = _space.Wrap(bank.Position + drift);
            if (bank.Age < bank.Lifetime)
                continue;
            _banks.RemoveAt(i);
            emit?.Invoke(new FogBankDissipated(now, bank.Id, bank.Position));
        }

        var rate = _banks.Count(b => !b.IsDissipating) < FewBanks ? FormationRateWhenThin : FormationRate;
        if (_formation && _random.Chance((float)(rate * hours)))
            TryForm(now, emit);
    }

    /// <summary>The Fog banks as they are now.</summary>
    public FogBank[] Snapshot() => _banks.Select(b => b.ToFogBank()).ToArray();

    /// <summary>Places a fully formed Fog bank that lingers for <paramref name="lingerFor"/>, then dissipates.</summary>
    public FogBank Place(Vector2 position, float radius, float density, TimeSpan lingerFor, StoreClock now, Action<DomainEvent> emit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        if (density is <= 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(density), "A Fog bank's density lies above 0, up to 1.");
        ArgumentOutOfRangeException.ThrowIfLessThan(lingerFor, TimeSpan.Zero);
        var bank = new Bank(_nextId++, _space.Wrap(position), radius, density,
            forming: 1.5, lingering: lingerFor.TotalHours, dissipating: 2.0) { Age = 1.5 };
        _banks.Add(bank);
        var placed = bank.ToFogBank();
        emit(new FogBankFormed(now, placed));
        return placed;
    }

    /// <summary>Removes every Fog bank at once, with no events.</summary>
    public void Clear() => _banks.Clear();

    /// <summary>Whether new Fog banks form on their own.</summary>
    public void SetFormation(bool enabled) => _formation = enabled;

    /// <summary>Sets the wind and holds it steady from then on.</summary>
    public void HoldWind(Vector2 velocity)
    {
        _windHeld = true;
        _windSpeed = _targetSpeed = velocity.Length();
        _windHeading = _targetHeading = velocity == Vector2.Zero ? _windHeading : MathF.Atan2(velocity.Y, velocity.X);
    }

    private void UpdateWind(double hours)
    {
        if (_windHeld)
            return;
        if (_random.Chance((float)(WindShiftsPerHour * hours)))
        {
            var shift = MaxWindShiftDegrees * MathF.PI / 180f;
            _targetHeading = _windHeading + _random.Range(-shift, shift);
            _targetSpeed = _random.Range(MinWindSpeed, MaxWindSpeed);
        }
        var turn = WindTurnDegreesPerHour * MathF.PI / 180f * (float)hours;
        _windHeading += Math.Clamp(_targetHeading - _windHeading, -turn, turn);
        var change = WindSpeedChangePerHour * (float)hours;
        _windSpeed += Math.Clamp(_targetSpeed - _windSpeed, -change, change);
    }

    private void TryForm(StoreClock now, Action<DomainEvent>? emit)
    {
        var radius = _random.Range(MinRadius, MaxRadius);
        var covered = _banks.Sum(b => b.MaxArea);
        if (covered + Bank.MaxAreaFor(radius) > MaxCoverage * _space.Size * _space.Size)
            return;
        var position = new Vector2(_random.Range(0f, _space.Size), _random.Range(0f, _space.Size));
        var bank = new Bank(_nextId++, _space.Wrap(position), radius, _random.Range(MinPeakDensity, 1f),
            forming: _random.Range(1f, 2f), lingering: _random.Range(4f, 10f), dissipating: _random.Range(1.5f, 3f));
        _banks.Add(bank);
        var formed = bank.ToFogBank();
        emit?.Invoke(new FogBankFormed(now, formed));
    }

    private sealed class Bank(int id, Vector2 position, float radius, float peakDensity, double forming, double lingering, double dissipating)
    {
        public int Id { get; } = id;
        public Vector2 Position { get; set; } = position;
        public double Age { get; set; }
        public double Lifetime => forming + lingering + dissipating;
        public bool IsDissipating => Age >= forming + lingering;

        /// <summary>The most floor the bank will ever cover: its spread-out size while dissipating.</summary>
        public double MaxArea => MaxAreaFor(radius);

        public static double MaxAreaFor(float radius) => Math.PI * Math.Pow(radius * (1f + DissipatingSpread), 2);

        public FogBank ToFogBank()
        {
            if (Age < forming)
            {
                var grown = SmoothStep(Age / forming);
                return new FogBank(Id, Position, radius * (FormingStartSize + (1f - FormingStartSize) * grown),
                    peakDensity * grown, FogBankStage.Forming);
            }
            if (Age < forming + lingering)
                return new FogBank(Id, Position, radius, peakDensity, FogBankStage.Lingering);
            var thinned = SmoothStep((Age - forming - lingering) / dissipating);
            return new FogBank(Id, Position, radius * (1f + DissipatingSpread * thinned),
                peakDensity * (1f - thinned), FogBankStage.Dissipating);
        }

        private static float SmoothStep(double x)
        {
            var t = (float)Math.Clamp(x, 0.0, 1.0);
            return t * t * (3f - 2f * t);
        }
    }
}
