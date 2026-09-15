using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using QueryMongo.Core;
using QueryMongo.Core.Services;

namespace QueryMongo.App.ViewModels;

/// <summary>
/// The Performance tab: server operation rates, connections, memory and the list of
/// operations running right now.
/// </summary>
public sealed partial class PerformanceViewModel : ObservableObject, IDisposable
{
    private readonly AdminService _admin;
    private readonly DispatcherTimer _timer;
    private ServerMetrics? _previous;
    private bool _sampling;

    public PerformanceViewModel(AdminService admin)
    {
        _admin = admin;

        // Counters are cumulative, so rates need two samples a known interval apart.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await SampleAsync().ConfigureAwait(true);

        Status = "Not sampling.";
    }

    public ObservableCollection<CurrentOperation> Operations { get; } = [];

    /// <summary>Recent operations-per-second values, oldest first, for the sparkline.</summary>
    public ObservableCollection<double> OperationHistory { get; } = [];

    private const int HistoryLength = 60;

    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial string Status { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    [ObservableProperty] public partial double InsertRate { get; set; }
    [ObservableProperty] public partial double QueryRate { get; set; }
    [ObservableProperty] public partial double UpdateRate { get; set; }
    [ObservableProperty] public partial double DeleteRate { get; set; }
    [ObservableProperty] public partial double CommandRate { get; set; }

    // Pre-formatted for the tiles; XAML has no number formatting in x:Bind.
    [ObservableProperty] public partial string InsertRateText { get; set; } = "0";
    [ObservableProperty] public partial string QueryRateText { get; set; } = "0";
    [ObservableProperty] public partial string UpdateRateText { get; set; } = "0";
    [ObservableProperty] public partial string DeleteRateText { get; set; } = "0";
    [ObservableProperty] public partial string CommandRateText { get; set; } = "0";

    [ObservableProperty] public partial string NetworkIn { get; set; } = "—";
    [ObservableProperty] public partial string NetworkOut { get; set; } = "—";
    [ObservableProperty] public partial string Connections { get; set; } = "—";
    [ObservableProperty] public partial string Memory { get; set; } = "—";
    [ObservableProperty] public partial string Uptime { get; set; } = "—";

    [ObservableProperty] public partial bool ShowIdleOperations { get; set; }

    [RelayCommand]
    private void Start()
    {
        if (IsRunning) return;

        IsRunning = true;
        Status = "Sampling every 2 seconds.";
        _timer.Start();
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _timer.Stop();
        Status = "Paused.";
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    [RelayCommand]
    public async Task SampleAsync()
    {
        // A slow server must not queue overlapping samples behind each other.
        if (_sampling) return;
        _sampling = true;

        try
        {
            var current = await _admin.GetMetricsAsync().ConfigureAwait(true);

            if (_previous is { } previous)
            {
                var rates = ServerRates.Between(previous, current);

                InsertRate = rates.Insert;
                QueryRate = rates.Query;
                UpdateRate = rates.Update;
                DeleteRate = rates.Delete;
                CommandRate = rates.Command;

                InsertRateText = Format(rates.Insert);
                QueryRateText = Format(rates.Query);
                UpdateRateText = Format(rates.Update);
                DeleteRateText = Format(rates.Delete);
                CommandRateText = Format(rates.Command);

                NetworkIn = $"{ByteSize.Format((long)rates.NetworkInPerSecond)}/s";
                NetworkOut = $"{ByteSize.Format((long)rates.NetworkOutPerSecond)}/s";

                OperationHistory.Add(rates.Insert + rates.Query + rates.Update + rates.Delete);
                while (OperationHistory.Count > HistoryLength) OperationHistory.RemoveAt(0);
            }

            Connections = $"{current.CurrentConnections:N0} in use, {current.AvailableConnections:N0} available";
            Memory = $"{current.ResidentMemoryMb:N0} MB resident, {current.VirtualMemoryMb:N0} MB virtual";
            Uptime = FormatUptime(current.Uptime);

            _previous = current;

            var operations = await _admin
                .GetCurrentOperationsAsync(ShowIdleOperations).ConfigureAwait(true);

            Operations.Clear();
            foreach (var op in operations) Operations.Add(op);

            ErrorMessage = null;
        }
        catch (Exception e)
        {
            ErrorMessage = e.Message;
            Stop();
        }
        finally
        {
            _sampling = false;
        }
    }

    /// <summary>Kills a running operation. The caller confirms first.</summary>
    public async Task<string?> KillAsync(CurrentOperation operation)
    {
        try
        {
            await _admin.KillOperationAsync(operation.OperationId).ConfigureAwait(true);
            await SampleAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private static string Format(double rate) => rate >= 100 ? $"{rate:N0}" : $"{rate:N1}";

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                : $"{(int)uptime.TotalMinutes}m";

    public void Dispose() => _timer.Stop();
}
