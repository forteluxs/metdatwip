using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Metdatwip.App.ViewModels;

namespace Metdatwip.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static void OnDragOver(object? _, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        e.Handled = true;
    }

    private async void OnDrop(object? _, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var paths = new List<string>();

        try
        {
            var storageItems = e.DataTransfer.TryGetFiles();
            if (storageItems != null)
            {
                foreach (var item in storageItems)
                {
                    if (item?.Path != null)
                    {
                        var localPath = item.Path.LocalPath;
                        if (!string.IsNullOrWhiteSpace(localPath))
                        {
                            paths.Add(localPath);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore error
        }

        if (paths.Count > 0)
        {
            await viewModel.HandleDroppedPathsAsync(paths);
        }
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File to Inspect",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("All Supported Formats")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff", "*.webp", "*.heic", "*.heif", "*.pdf", "*.docx", "*.xlsx", "*.pptx", "*.mp3", "*.wav", "*.mp4", "*.mov", "*.m4v", "*.mkv", "*.webm" }
                },
                new FilePickerFileType("Images (*.jpg, *.png, *.webp, *.heic)")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff", "*.webp", "*.heic", "*.heif" }
                },
                new FilePickerFileType("PDF & Office Documents (*.pdf, *.docx, *.xlsx, *.pptx)")
                {
                    Patterns = new[] { "*.pdf", "*.docx", "*.xlsx", "*.pptx" }
                },
                new FilePickerFileType("Audio & Video (*.mp3, *.wav, *.mp4, *.mov, *.mkv, *.webm)")
                {
                    Patterns = new[] { "*.mp3", "*.wav", "*.mp4", "*.mov", "*.m4v", "*.mkv", "*.webm" }
                }
            }
        });

        if (files.Count > 0)
        {
            var selectedPath = files[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                await viewModel.HandleDroppedPathsAsync(new[] { selectedPath });
            }
        }
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder to Inspect",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var selectedPath = folders[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                await viewModel.HandleDroppedPathsAsync(new[] { selectedPath });
            }
        }
    }
}
