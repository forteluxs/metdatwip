using Avalonia.Controls;
using Avalonia.Input;
using Metawipe.App.ViewModels;

namespace Metawipe.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? _, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private async void OnDrop(object? _, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var storageItems = e.DataTransfer.TryGetFiles();
        if (storageItems is null || storageItems.Length == 0)
        {
            await viewModel.HandleDroppedPathsAsync([]);
            return;
        }

        var paths = storageItems
            .Select(item => item.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        await viewModel.HandleDroppedPathsAsync(paths);
    }
}
