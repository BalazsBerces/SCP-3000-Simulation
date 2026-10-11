using Godot;

namespace Scp3008.Game;

/// <summary>
/// Development aid for unattended checks. With user arguments after "--":
///   --capture=&lt;png path&gt;   save a screenshot after <c>--capture-after</c> seconds (default 3) and quit;
///   --fps-report           print the average frame rate over the capture window.
/// Does nothing in normal runs.
/// </summary>
public partial class DevCapture : Node
{
    private string? _capturePath;
    private double _captureAfter = 3.0;
    private bool _fpsReport;
    private double _elapsed;
    private int _frames;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture="))
                _capturePath = arg["--capture=".Length..];
            else if (arg.StartsWith("--capture-after="))
                _captureAfter = double.Parse(arg["--capture-after=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg == "--fps-report")
                _fpsReport = true;
        }
        SetProcess(_capturePath is not null || _fpsReport);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _frames++;
        if (_elapsed < _captureAfter)
            return;

        if (_fpsReport)
            GD.Print($"FPS average over {_elapsed:0.0}s: {_frames / _elapsed:0.0}");
        if (_capturePath is not null)
        {
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(_capturePath);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {_capturePath}" : $"Screenshot failed: {error}");
        }
        GetTree().Quit();
    }
}
