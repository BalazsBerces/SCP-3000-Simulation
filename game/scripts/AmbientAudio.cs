using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Store's ambient sound, synthesised in code (no audio assets): the hum of the fluorescent
/// lights while the Store is open, a low drone in the dark of Closing time, and distant footsteps
/// that echo through the shelving: rare and light while the lights are on, heavy and frequent at
/// Closing time. A relay clunk marks the lights going out and coming back on.
/// </summary>
public partial class AmbientAudio : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public StoreAtmosphere Atmosphere { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    private const int SampleRate = 22050;
    private const string DistantBus = "Distant";

    private readonly RandomNumberGenerator _random = new();
    private AudioStreamPlayer _hum = null!;
    private AudioStreamPlayer _drone = null!;
    private AudioStreamPlayer3D[] _steps = [];
    private AudioStreamPlayer3D _relay = null!;
    private AudioStreamWav[] _stepSounds = [];
    private int _nextStepPlayer;
    private double _untilNextWalk = 3.0;
    private int _stepsLeft;
    private double _untilNextStep;
    private Vector3 _walker;
    private Vector3 _walkDirection;
    private float _walkHeaviness;

    public override void _Ready()
    {
        _random.Randomize();
        CreateDistantBus();

        _hum = AddLoop("FluorescentHum", HumLoop());
        _drone = AddLoop("ClosingDrone", DroneLoop());
        _stepSounds = [.. Enumerable.Range(0, 4).Select(i => Footstep(i))];
        _steps = [.. Enumerable.Range(0, 6).Select(i => AddDistantPlayer($"Footsteps{i}"))];
        _relay = AddDistantPlayer("Relay");
        _relay.Stream = RelayClunk();

        Host.DomainEventReceived += domainEvent =>
        {
            if (domainEvent is ClosingTimeBegan or StoreReopened)
                PlayDistant(_relay, Rig.Camera.GlobalPosition + new Vector3(0f, -10f, 0f), 0f, 1f);
        };
        UpdateLoops();
    }

    public override void _Process(double delta)
    {
        UpdateLoops();
        UpdateFootsteps(delta);
    }

    /// <summary>A one-line summary for dev reports.</summary>
    public string Describe() =>
        $"hum {_hum.VolumeDb:0.0} dB, drone {_drone.VolumeDb:0.0} dB, playing {_hum.Playing && _drone.Playing}, " +
        $"next footsteps {(_stepsLeft > 0 ? "now" : $"in {_untilNextWalk:0.0} s")} (heaviness {Darkness:0.00})";

    private float Darkness => 1f - Atmosphere.LightLevel;

    private void UpdateLoops()
    {
        var light = Atmosphere.LightLevel;
        _hum.VolumeDb = Mathf.LinearToDb(0.45f * light + 0.0001f);
        _drone.VolumeDb = Mathf.LinearToDb(0.55f * Darkness + 0.0001f);
    }

    // Footsteps come in walks: someone (or something) out of sight takes a few steps and stops.
    private void UpdateFootsteps(double delta)
    {
        if (_stepsLeft == 0)
        {
            _untilNextWalk -= delta;
            if (_untilNextWalk > 0)
                return;
            StartWalk();
        }
        _untilNextStep -= delta;
        if (_untilNextStep > 0)
            return;

        _walker += _walkDirection * Mathf.Lerp(0.75f, 0.9f, _walkHeaviness);
        var sound = _stepSounds[_random.RandiRange(0, _stepSounds.Length - 1)];
        var player = _steps[_nextStepPlayer];
        _nextStepPlayer = (_nextStepPlayer + 1) % _steps.Length;
        player.Stream = sound;
        PlayDistant(player, Rig.Camera.GlobalPosition + _walker,
            Mathf.Lerp(-12f, -2f, _walkHeaviness) + _random.RandfRange(-2f, 1f),
            Mathf.Lerp(1.05f, 0.8f, _walkHeaviness) * _random.RandfRange(0.95f, 1.05f));

        _stepsLeft--;
        _untilNextStep = Mathf.Lerp(0.5f, 0.8f, _walkHeaviness) * _random.RandfRange(0.92f, 1.08f);
        if (_stepsLeft == 0)
            _untilNextWalk = Mathf.Lerp(_random.RandfRange(6f, 16f), _random.RandfRange(1.5f, 5f), Darkness);
    }

    private void StartWalk()
    {
        _walkHeaviness = Darkness;
        var angle = _random.RandfRange(0f, Mathf.Tau);
        var distance = _random.RandfRange(15f, 40f);
        _walker = new Vector3(Mathf.Cos(angle) * distance, -3f, Mathf.Sin(angle) * distance);
        var heading = _random.RandfRange(0f, Mathf.Tau);
        _walkDirection = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading));
        _stepsLeft = _random.RandiRange(3, 10);
        _untilNextStep = 0;
    }

    private void PlayDistant(AudioStreamPlayer3D player, Vector3 position, float volumeDb, float pitch)
    {
        player.GlobalPosition = position;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitch;
        player.Play();
    }

    private AudioStreamPlayer AddLoop(string name, AudioStreamWav stream)
    {
        var player = new AudioStreamPlayer { Name = name, Stream = stream, Autoplay = true };
        AddChild(player);
        return player;
    }

    private AudioStreamPlayer3D AddDistantPlayer(string name)
    {
        // Positions are kept a set distance from the camera, so attenuation does not depend on zoom.
        var player = new AudioStreamPlayer3D
        {
            Name = name, Bus = DistantBus,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            UnitSize = 25f, MaxDistance = 0f, PanningStrength = 0.8f,
        };
        AddChild(player);
        return player;
    }

    // Footsteps and the relay echo through a long reverb with the highs muffled by the shelving.
    private static void CreateDistantBus()
    {
        if (AudioServer.GetBusIndex(DistantBus) >= 0)
            return;
        AudioServer.AddBus();
        var bus = AudioServer.BusCount - 1;
        AudioServer.SetBusName(bus, DistantBus);
        AudioServer.SetBusSend(bus, "Master");
        AudioServer.AddBusEffect(bus, new AudioEffectLowPassFilter { CutoffHz = 1400f });
        AudioServer.AddBusEffect(bus, new AudioEffectReverb
        {
            RoomSize = 0.95f, Damping = 0.6f, Spread = 1f, Wet = 0.55f, Dry = 0.5f, PredelayMsec = 60f,
        });
    }

    // Mains hum of the tubes at 100 Hz with harmonics and a slow beat; every component repeats a
    // whole number of times in the 3 s loop, so it loops without a click.
    private static AudioStreamWav HumLoop()
    {
        const double seconds = 3.0;
        var samples = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            var buzz = Math.Tanh(2.5 * Math.Sin(Math.Tau * 100.0 * t)) * 0.35;
            samples[i] = (float)(buzz
                + 0.25 * Math.Sin(Math.Tau * (100.0 + 1.0 / 3.0) * t)
                + 0.15 * Math.Sin(Math.Tau * 200.0 * t + 0.7)
                + 0.08 * Math.Sin(Math.Tau * 300.0 * t + 1.9)
                + 0.03 * Math.Sin(Math.Tau * 1200.0 * t) * (0.6 + 0.4 * Math.Sin(Math.Tau * t / seconds)));
        }
        return Wav(samples, loop: true);
    }

    // A dark rumble built from many low partials with random phases (a loopable stand-in for brown
    // noise), slowly swelling.
    private static AudioStreamWav DroneLoop()
    {
        const int seconds = 6;
        var random = new Random(3008);
        var samples = new double[SampleRate * seconds];
        for (var k = 20 * seconds; k <= 160 * seconds; k += 4)
        {
            var frequency = (double)k / seconds;
            var amplitude = 1.0 / frequency;
            var phase = random.NextDouble() * Math.Tau;
            var step = Math.Tau * frequency / SampleRate;
            for (var i = 0; i < samples.Length; i++)
                samples[i] += amplitude * Math.Sin(step * i + phase);
        }
        var peak = samples.Max(Math.Abs);
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            var swell = 0.75 + 0.25 * Math.Sin(Math.Tau * t / 3.0);
            result[i] = (float)(samples[i] / peak * 0.8 * swell + 0.12 * Math.Sin(Math.Tau * 41.0 * t));
        }
        return Wav(result, loop: true);
    }

    // A footstep: a low thud with a falling pitch and a short scuff of filtered noise.
    private static AudioStreamWav Footstep(int variant)
    {
        var random = new Random(variant + 1);
        var samples = new float[(int)(SampleRate * 0.4)];
        var baseFrequency = 70.0 + variant * 9.0;
        double phase = 0, noise = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            phase += Math.Tau * baseFrequency * (1.0 + 0.8 * Math.Exp(-t * 30.0)) / SampleRate;
            noise += (random.NextDouble() * 2.0 - 1.0 - noise) * 0.25;
            samples[i] = (float)(Math.Sin(phase) * Math.Exp(-t * 22.0) * 0.9 + noise * Math.Exp(-t * 60.0) * 1.6);
        }
        return Wav(samples, loop: false);
    }

    // The heavy clunk of a big lighting relay somewhere overhead.
    private static AudioStreamWav RelayClunk()
    {
        var random = new Random(42);
        var samples = new float[(int)(SampleRate * 1.2)];
        double noise = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            noise += (random.NextDouble() * 2.0 - 1.0 - noise) * 0.5;
            samples[i] = (float)(Math.Sin(Math.Tau * 55.0 * t) * Math.Exp(-t * 6.0) * 0.8
                + Math.Sin(Math.Tau * 140.0 * t) * Math.Exp(-t * 14.0) * 0.4
                + noise * Math.Exp(-t * 40.0) * 0.9);
        }
        return Wav(samples, loop: false);
    }

    private static AudioStreamWav Wav(float[] samples, bool loop)
    {
        var data = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            data[2 * i] = (byte)value;
            data[2 * i + 1] = (byte)(value >> 8);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = data,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? samples.Length : 0,
        };
    }
}
