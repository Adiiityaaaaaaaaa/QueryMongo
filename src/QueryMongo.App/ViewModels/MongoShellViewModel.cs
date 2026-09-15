using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QueryMongo.Core.Shell;

namespace QueryMongo.App.ViewModels;

/// <summary>One command and its output in the shell transcript.</summary>
public sealed class ShellEntry(string prompt, string command, string output, bool isError)
{
    public string Prompt { get; } = prompt;
    public string Command { get; } = command;
    public string Output { get; } = output;
    public bool IsError { get; } = isError;
    public bool HasOutput { get; } = !string.IsNullOrWhiteSpace(output);
}

/// <summary>
/// The shell pane: a transcript of commands and their results, with history recall
/// on the arrow keys.
/// </summary>
public sealed partial class MongoShellViewModel : ObservableObject
{
    private readonly ShellService _shell;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public MongoShellViewModel(ShellService shell)
    {
        _shell = shell;
        Input = "";

        Entries.Add(new ShellEntry("", "",
            "QueryMongo shell. Type help for the supported commands.", false));
    }

    public ObservableCollection<ShellEntry> Entries { get; } = [];

    [ObservableProperty] public partial string Input { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }

    public string Prompt => $"{_shell.CurrentDatabase}>";

    [RelayCommand]
    public async Task RunAsync()
    {
        var command = Input.Trim();
        if (command.Length == 0) return;

        // Record before running, so history is correct even if the command fails.
        _history.Remove(command);
        _history.Add(command);
        _historyIndex = _history.Count;

        var prompt = Prompt;
        Input = "";
        IsBusy = true;

        try
        {
            var result = await _shell.ExecuteAsync(command).ConfigureAwait(true);

            Entries.Add(new ShellEntry(prompt, command, result.Output, result.IsError));

            // use changes the database, so the prompt moves with it.
            OnPropertyChanged(nameof(Prompt));
        }
        catch (Exception e)
        {
            Entries.Add(new ShellEntry(prompt, command, e.Message, true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Walks back through previous commands, as a terminal's up arrow does.</summary>
    public void RecallPrevious()
    {
        if (_history.Count == 0) return;

        _historyIndex = Math.Max(0, _historyIndex - 1);
        Input = _history[_historyIndex];
    }

    public void RecallNext()
    {
        if (_history.Count == 0) return;

        _historyIndex++;

        if (_historyIndex >= _history.Count)
        {
            _historyIndex = _history.Count;
            Input = "";
            return;
        }

        Input = _history[_historyIndex];
    }

    [RelayCommand]
    private void Clear()
    {
        Entries.Clear();
        Entries.Add(new ShellEntry("", "", "Cleared. Type help for the supported commands.", false));
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        Input = "help";
        await RunAsync().ConfigureAwait(true);
    }
}
