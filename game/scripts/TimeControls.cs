using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Observer time controls and Store clock. Buttons or keys: Space toggles pause,
/// 1-5 select 1x, 2x, 4x, 8x and 16x.
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

    private readonly Dictionary<SimulationSpeed, Button> _buttons = new();
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
        foreach (var (speed, label, _) in Speeds)
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
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
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
        var phase = StoreHours.PhaseAt(timeOfDay) == DayPhase.Open ? "Open" : "Closing time";
        _clockLabel.Text = $"Day {day}   {timeOfDay.Hours:00}:{timeOfDay.Minutes:00}   {phase}";
        _dayProgress.Value = timeOfDay.TotalDays;
        // Without a signal the button group does not unpress the others, so set every button.
        foreach (var (speed, button) in _buttons)
            button.SetPressedNoSignal(speed == Host.Speed);
    }

    private void SetSpeed(SimulationSpeed speed)
    {
        if (speed != SimulationSpeed.Paused)
            _speedBeforePause = speed;
        Host.Speed = speed;
    }
}
