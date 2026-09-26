using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Saves;
using Godot;

namespace Headland.Game.Saves;

/// <summary>
/// Save slots in user://saves: quicksave and quickload, autosave, and the game to start with after a load.
/// Snapshots are taken on the main thread and written in the background. Loading builds the new game first, then
/// reloads the scene around it, so a save that fails to load leaves the current game running.
/// </summary>
public partial class SaveManager : Node
{
    public const string QuickSlot = "quicksave";
    public const string AutoSlot = "autosave";

    private static SaveStore? _store;
    private static (LoadedGame game, string slot)? _pending;
    private Task? _writing;
    private string _writingSlot = "";
    private double _sinceAutosave;

    public static SaveStore Store => _store ??= new SaveStore(ProjectSettings.GlobalizePath("user://saves"));
    public static string GameVersion => ProjectSettings.GetSetting("application/config/version").AsString();

    public Simulation Sim { get; init; } = null!;
    /// <summary>Real minutes between autosaves; 0 turns autosave off.</summary>
    public double AutosaveMinutes { get; set; } = 10;

    /// <summary>The game a load just built, if any (taken once, when the scene starts again).</summary>
    public static (LoadedGame game, string slot)? TakePending()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }

    /// <summary>Reads and builds a saved game; throws <see cref="SaveException"/> when it can't.</summary>
    public static LoadedGame Read(ContentDatabase content, string slot) =>
        SaveStore.IsValidSlot(slot) ? SaveGame.Load(content, Store.Read(slot)) : throw new SaveException($"'{slot}' is not a save name");

    public void Save(string slot)
    {
        _writing?.Wait();
        var file = SaveGame.Capture(Sim, GameVersion);
        _writingSlot = slot;
        _writing = Task.Run(() => Store.Write(slot, file));
    }

    public void Load(string slot)
    {
        try
        {
            _pending = (Read(Sim.Content, slot), slot);
        }
        catch (SaveException e)
        {
            Sim.Notifications.Post($"Could not load {slot}: {e.Message}", Severity.Warning);
            return;
        }
        _writing?.Wait();
        GetTree().ReloadCurrentScene();
    }

    public override void _Process(double delta)
    {
        if (_writing is { IsCompleted: true } done)
        {
            _writing = null;
            if (done.Exception?.InnerException is { } e)
                Sim.Notifications.Post($"Saving failed: {e.Message}", Severity.Warning);
            else
                Sim.Notifications.Post(_writingSlot == AutoSlot ? "Autosaved" : $"Game saved ({_writingSlot})", Severity.Good);
        }

        if (AutosaveMinutes <= 0) return;
        _sinceAutosave += delta;
        if (_sinceAutosave < AutosaveMinutes * 60) return;
        _sinceAutosave = 0;
        Save(AutoSlot);
    }

    public override void _ExitTree() => _writing?.Wait();
}
