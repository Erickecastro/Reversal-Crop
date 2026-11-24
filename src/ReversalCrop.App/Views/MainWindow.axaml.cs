using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Platform.Storage;
using ReversalCrop.App.ViewModels;

namespace ReversalCrop.App.Views;

public partial class MainWindow : Window
{
    private Point? _dragStart;
    private Point _previewSize;
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Preview_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStart = e.GetPosition(this);
            if (sender is Control control)
            {
                _previewSize = new Point(control.Bounds.Width, control.Bounds.Height);
                e.Pointer.Capture(control);
            }
        }
    }

    private void Preview_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not Point previous || DataContext is not MainViewModel vm) return;
        Point current = e.GetPosition(this);
        double cropSpanX = Math.Max(1, _previewSize.X * (1 - 100 / vm.Zoom));
        double cropSpanY = Math.Max(1, _previewSize.Y * (1 - 100 / vm.Zoom));
        vm.Pan((current.X - previous.X) * 200 / cropSpanX,
            (current.Y - previous.Y) * 200 / cropSpanY);
        _dragStart = current;
    }

    private void Preview_PointerReleased(object? sender, PointerReleasedEventArgs e)
    { _dragStart = null; e.Pointer.Capture(null); }

    private void Preview_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    { if (DataContext is MainViewModel vm) vm.AdjustZoom(e.Delta.Y * 5); }

    private async void OpenImage_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Imagens")
                        {
                            Patterns =
                            [
                                "*.png", "*.jpg", "*.jpeg",
                                "*.webp", "*.bmp"
                            ]
                        }
                    ]
                });

            if (files.Count == 0)
                return;

            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);

            if (DataContext is MainViewModel viewModel)
                viewModel.SetImage(memory.ToArray(), files[0].Name);
        }
        catch (Exception)
        {
            if (DataContext is MainViewModel viewModel)
                viewModel.StatusMessage = "Erro ao abrir imagem.";
        }
    }

    private async void Export512_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExportAsync(512, 4);
    }

    private async void Export2508_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExportAsync(2508, 2);
    }

    private async void Export_Click(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExportAsync(2508, 2);
    }

    private async Task ExportAsync(int target, int supersample)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Exportar ícone",
                    SuggestedFileName = $"reversal-{target}x{target}.png",
                    DefaultExtension = "png",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("Imagem PNG")
                        {
                            Patterns = ["*.png"],
                            MimeTypes = ["image/png"]
                        }
                    ]
                });

            if (file is null)
                return;

            viewModel.StatusMessage = "Renderizando imagem...";

            byte[] png = await viewModel.RenderExportAsync(
                target, supersample);

            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(png);

            viewModel.StatusMessage =
                $"Imagem {target} × {target} exportada com sucesso.";
        }
        catch (Exception)
        {
            viewModel.StatusMessage = "Erro ao exportar imagem.";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.Dispose();

        base.OnClosed(e);
    }
}
