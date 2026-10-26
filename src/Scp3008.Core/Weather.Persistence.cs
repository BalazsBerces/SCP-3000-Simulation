using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Weather
{
    internal sealed record BankState(int Id, Vector2 Position, float Radius, float PeakDensity,
        double Forming, double Lingering, double Dissipating, double Age);

    internal sealed record State(ulong Random, int NextId, float Heading, float Speed,
        float TargetHeading, float TargetSpeed, bool Held, bool Formation, BankState[] Banks);

    internal State Save() => new(_random.State, _nextId, _windHeading, _windSpeed,
        _targetHeading, _targetSpeed, _windHeld, _formation, _banks.Select(b => b.Save()).ToArray());

    internal void Restore(State state)
    {
        _random.State = state.Random;
        _nextId = state.NextId;
        _windHeading = state.Heading;
        _windSpeed = state.Speed;
        _targetHeading = state.TargetHeading;
        _targetSpeed = state.TargetSpeed;
        _windHeld = state.Held;
        _formation = state.Formation;
        _banks.Clear();
        _banks.AddRange(state.Banks.Select(b => new Bank(b.Id, b.Position, b.Radius, b.PeakDensity,
            b.Forming, b.Lingering, b.Dissipating) { Age = b.Age }));
    }
}
