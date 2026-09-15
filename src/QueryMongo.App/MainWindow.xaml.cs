using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QueryMongo.App.ViewModels;

namespace QueryMongo.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Shell = App.Services.GetRequiredService<ShellViewModel>();

        Title = "QueryMongo";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));

        _ = Shell.LoadConnectionsAsync();
    }

    public ShellViewModel Shell { get; }
}
