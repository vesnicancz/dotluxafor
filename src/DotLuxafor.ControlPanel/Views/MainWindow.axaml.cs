using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DotLuxafor.ControlPanel.ViewModels;

namespace DotLuxafor.ControlPanel.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.Red) or
                    nameof(MainViewModel.Green) or nameof(MainViewModel.Blue))
                {
                    UpdateColorPreview(vm);
                }
            };
            UpdateColorPreview(vm);
        }
    }

    private void UpdateColorPreview(MainViewModel vm)
    {
        var border = this.FindControl<Border>("ColorPreviewBorder");
        if (border is not null)
        {
            border.Background = new SolidColorBrush(Color.FromRgb((byte)vm.Red, (byte)vm.Green, (byte)vm.Blue));
        }
    }

    private void LedRadioChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tagStr } &&
            int.TryParse(tagStr, out var index) &&
            DataContext is MainViewModel vm)
        {
            vm.SelectedLedIndex = index;
        }
    }
}
