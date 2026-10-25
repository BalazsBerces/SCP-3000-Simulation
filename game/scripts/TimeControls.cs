using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Observer time controls and Store clock. Buttons or keys: Space toggles pause,
/// 1-5 select 1x, 2x, 4x, 8x and 16x. The Night/Day button or N jumps the clock forward to the
/// middle of Closing time (lights out) or of the open day, to check either lighting quickly.
/// </summary>
public partial class TimeControls : CanvasLayer
{
    [Export] public SimulationHost Host { get; set; } = null!;

    private static readonly (SimulationSpeed Speed, string Label, Key Key)[] Speeds =
    [
        (SimulationSpeed.Paused, "Pause", Key.Space),
        (SimulationSpeed.Normal, "1x", Key.Key1),
        (SimulationSpeed.Fast2, "2x", Key.Key2),
        (SimulationSpeed.Fast4, "4x", Key.Key3),
        (SimulationSpeed.Fast8, "8x", Key.Key4),
        (SimulationSpeed.Fast16, "16x", Key.Key5),
    ];

    /// <summary>Where the Night/Day button jumps: well past the lights' fade in either direction.</summary>
    private static readonly TimeSpan Night = TimeSpan.FromHours(23);
    private static readonly TimeSpan Day = TimeSpan.FromHours(12);

    private readonly Dictionary<SimulationSpeed, Button> _buttons = new();
    private Button _nightButton = null!;
    private Label _clockLabel = null!;
    private ProgressBar _dayProgress = null!;
    private SimulationSpeed _speedBeforePause = SimulationSpeed.Normal;

    public override void _Ready()
    {
        var panel = new PanelContainer { Position = new Vector2(16, 16) };
        AddChild(panel);
        var column = new VBoxContainer();
        panel.AddChild(column);

        _clockLabel = new Label { Name = "Clock" };
        _clockLabel.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(_clockLabel);

        _dayProgress = new ProgressBar
        {
            Name = "DayProgress",
            MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
        };
        column.AddChild(_dayProgress);

        var row = new HBoxContainer();
        column.AddChild(row);
        var group = new ButtonGroup();
        var speeds = Host.Snapshot.VillagerSandbox
            ? Speeds.Append((SimulationSpeed.Fast60, "60x", Key.Key6)) : Speeds;
        foreach (var (speed, label, _) in speeds)
        {
            var button = new Button
            {
                Text = label, ToggleMode = true, ButtonGroup = group,
                FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(52, 0),
            };
            button.Pressed += () => SetSpeed(speed);
            row.AddChild(button);
            _buttons[speed] = button;
        }
        if (Host.Snapshot.VillagerSandbox)
        {
            var step = new Button { Text = "Step", FocusMode = Control.FocusModeEnum.None, TooltipText = "Pause and advance one simulation tick (7.2 Store seconds)" };
            step.Pressed += Step;
            row.AddChild(step);
        }
        _nightButton = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(64, 0) };
        _nightButton.Pressed += ToggleNight;
        row.AddChild(_nightButton);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (Host.Snapshot.VillagerSandbox && key.Keycode is Key.Key6 or Key.Period)
        {
            if (key.Keycode == Key.Key6)
                SetSpeed(SimulationSpeed.Fast60);
            else
                Step();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.N)
        {
            ToggleNight();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Space)
        {
            SetSpeed(Host.Speed == SimulationSpeed.Paused ? _speedBeforePause : SimulationSpeed.Paused);
            GetViewport().SetInputAsHandled();
            return;
        }
        foreach (var (speed, _, shortcut) in Speeds)
        {
            if (shortcut == key.Keycode && speed != SimulationSpeed.Paused)
            {
                SetSpeed(speed);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Process(double delta)
    {
        var storeTime = Host.InterpolatedStoreTime;
        var day = (int)storeTime.TotalDays + 1;
        var timeOfDay = TimeSpan.FromTicks(storeTime.Ticks % TimeSpan.TicksPerDay);
        var open = StoreHours.PhaseAt(timeOfDay) == DayPhase.Open;
        var phase = open ? "Open" : "Closing time";
        _nightButton.Text = open ? "Night" : "Day";
        _clockLabel.Text = $"Day {day}   {timeOfDay.Hours:00}:{timeOfDay.Minutes:00}   {phase}";
        _dayProgress.Value = timeOfDay.TotalDays;
        // Without a signal the button group does not unpress the others, so set every button.
        foreach (var (speed, button) in _buttons)
            button.SetPressedNoSignal(speed == Host.Speed);
    }

    /// <summary>Runs the clock forward to the next Closing time while open, or to the next open day.</summary>
    private void ToggleNight()
    {
        var timeOfDay = TimeSpan.FromTicks(Host.InterpolatedStoreTime.Ticks % TimeSpan.TicksPerDay);
        Host.AdvanceTo(StoreHours.PhaseAt(timeOfDay) == DayPhase.Open ? Night : Day);
    }

    private void Step()
    {
        SetSpeed(SimulationSpeed.Paused);
        Host.SkipTime(StoreClock.TickDuration);
    }

    private void SetSpeed(SimulationSpeed speed)
    {
        if (speed != SimulationSpeed.Paused)
            _speedBeforePause = speed;
        Host.Speed = speed;
    }
}
