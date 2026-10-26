using System.Text.Json;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // Windowed run: --pause --save-load-check=directory, optionally with --villager-sandbox.
    private async void CheckSaveLoad(string directory)
    {
        var lines = new List<string>();
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            directory = System.IO.Path.GetFullPath(directory);
            void Check(bool passed, string message)
            {
                if (!passed) throw new InvalidOperationException(message);
                lines.Add("PASS " + message);
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Host.Speed = SimulationSpeed.Paused;
            await Wait(2);
            var controls = GetTree().Root.FindChildren("*", "", true, false).OfType<TimeControls>().Single();
            var buttons = controls.FindChildren("*", "Button", true, false).Cast<Button>().ToArray();
            var saveButton = buttons.Single(b => b.Name == "SaveWorld");
            var loadButton = buttons.Single(b => b.Name == "LoadWorld");
            var saveDialog = controls.GetNode<FileDialog>("SaveWorldDialog");
            var loadDialog = controls.GetNode<FileDialog>("LoadWorldDialog");
            var status = controls.FindChild("SaveStatus", true, false) as Label;
            var json = new JsonSerializerOptions { IncludeFields = true };
            Host.SkipTime(TimeSpan.FromHours(0.1));
            var before = JsonSerializer.Serialize(Host.Snapshot, json);
            var savePath = System.IO.Path.Combine(directory, "world.scp3008.json");
            saveButton.EmitSignal(Button.SignalName.Pressed);
            await Wait(0.3);
            Check(saveDialog.Visible, "Observer Save world button opens file selection");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Captured " + name);
            }
            await Capture("save-dialog");
            saveDialog.Hide();
            saveDialog.EmitSignal(FileDialog.SignalName.FileSelected, savePath);
            Check(System.IO.File.Exists(savePath) && status!.Text == "World saved.", "Observer file selection saves world");
            // Exercise replacement of an existing save, too.
            saveDialog.EmitSignal(FileDialog.SignalName.FileSelected, savePath);
            Check(!System.IO.Directory.GetFiles(directory, "*.tmp").Any(), "Save replacement leaves no temporary file");
            Host.SkipTime(TimeSpan.FromHours(0.5));
            loadButton.EmitSignal(Button.SignalName.Pressed);
            Check(loadDialog.Visible, "Observer Load world button opens file selection");
            loadDialog.Hide();
            loadDialog.EmitSignal(FileDialog.SignalName.FileSelected, savePath);
            for (var i = 0; Host.IsGenerating && i < 450; i++) await Wait(0.1);
            Check(!Host.IsGenerating && status!.Text == "World loaded.", "Observer load completes");
            Check(before == JsonSerializer.Serialize(Host.Snapshot, json), "Loaded snapshot matches the saved running world");
            Check(Host.Layout == Host.Snapshot.PillarLayout && Host.Seed == Host.Snapshot.Seed, "Host settings reflect loaded world");
            await Wait(0.5);
            await Capture("loaded-world");
            var badPath = System.IO.Path.Combine(directory, "incompatible.scp3008.json");
            System.IO.File.WriteAllText(badPath, "{\"Version\":999}");
            var retained = Host.Snapshot;
            loadDialog.EmitSignal(FileDialog.SignalName.FileSelected, badPath);
            for (var i = 0; Host.IsGenerating && i < 450; i++) await Wait(0.1);
            var errorDialog = controls.GetNode<AcceptDialog>("SaveError");
            Check(!Host.IsGenerating && errorDialog.Visible && errorDialog.DialogText.Contains("Incompatible world save version 999"), "Incompatible version displayed clearly in Observer UI");
            Check(ReferenceEquals(retained, Host.Snapshot), "Failed load preserves current world");
            await Capture("version-error");
            errorDialog.Hide();
            Host.Speed = SimulationSpeed.Normal;
            var resume = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; Host.Snapshot.Clock.Tick == retained.Clock.Tick && i < 300; i++) await Wait(0.1);
            Check(Host.Snapshot.Clock.Tick > retained.Clock.Tick, "Loaded simulation thread continues advancing");
            lines.Add($"First resumed snapshot after {resume.Elapsed.TotalSeconds:0.00} s.");
            lines.Add("Save/load rendered check passed.");
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
        finally
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
        }
    }
}
