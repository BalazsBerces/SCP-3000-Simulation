using Godot;

namespace Scp3008.Game;

public partial class TimeControls
{
    private FileDialog _saveDialog = null!;
    private FileDialog _loadDialog = null!;
    private Label _saveStatus = null!;
    private AcceptDialog _saveError = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;
    private bool _waitingForLoad;

    private void AddPersistenceControls(VBoxContainer column)
    {
        var row = new HBoxContainer();
        column.AddChild(row);
        _saveButton = new Button { Name = "SaveWorld", Text = "Save world…", TooltipText = "Save the full world and its history" };
        _loadButton = new Button { Name = "LoadWorld", Text = "Load world…", TooltipText = "Continue a saved world" };
        row.AddChild(_saveButton);
        row.AddChild(_loadButton);
        _saveDialog = CreateSaveDialog("SaveWorldDialog", FileDialog.FileModeEnum.SaveFile);
        _loadDialog = CreateSaveDialog("LoadWorldDialog", FileDialog.FileModeEnum.OpenFile);
        _saveButton.Pressed += () => _saveDialog.PopupCenteredRatio(0.75f);
        _loadButton.Pressed += () => _loadDialog.PopupCenteredRatio(0.75f);
        _saveDialog.FileSelected += path => RunSaveOperation(() =>
        {
            Host.SaveWorld(path);
            _saveStatus.Text = "World saved.";
        });
        _loadDialog.FileSelected += path => RunSaveOperation(() =>
        {
            Host.LoadWorld(path);
            _waitingForLoad = true;
            _saveStatus.Text = "Loading world…";
        });
        _saveStatus = new Label { Name = "SaveStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0) };
        _saveStatus.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(_saveStatus);
        _saveError = new AcceptDialog { Name = "SaveError", Title = "World save/load failed" };
        AddChild(_saveError);
        Host.WorldOperationFailed += SaveFailed;
        Host.WorldCreated += SaveLoaded;
    }

    private FileDialog CreateSaveDialog(string name, FileDialog.FileModeEnum mode)
    {
        var dialog = new FileDialog
        {
            Name = name, Title = mode == FileDialog.FileModeEnum.SaveFile ? "Save world" : "Load world",
            FileMode = mode, Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.scp3008.json ; SCP-3008 world"],
            CurrentDir = OS.GetUserDataDir(), CurrentFile = mode == FileDialog.FileModeEnum.SaveFile ? "world.scp3008.json" : "",
        };
        AddChild(dialog);
        return dialog;
    }

    private void RunSaveOperation(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        { SaveFailed(error.Message); }
    }

    private void SaveFailed(string message)
    {
        _waitingForLoad = false;
        _saveStatus.Text = "World operation failed.";
        _saveError.DialogText = message;
        _saveError.PopupCentered(new Vector2I(640, 160));
    }

    private void SaveLoaded(Scp3008.Core.WorldSnapshot snapshot)
    {
        if (!_waitingForLoad) return;
        _waitingForLoad = false;
        _saveStatus.Text = "World loaded.";
    }

    public override void _ExitTree()
    {
        Host.WorldOperationFailed -= SaveFailed;
        Host.WorldCreated -= SaveLoaded;
    }
}
