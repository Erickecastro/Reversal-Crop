using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ReversalCrop.Core.Imaging;

namespace ReversalCrop.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private byte[]? _sourceBytes;
    private bool _updating;
    private CancellationTokenSource? _previewCancellation;

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial string FileName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial double Zoom { get; set; } = 100;

    [ObservableProperty]
    public partial double OffsetX { get; set; }

    [ObservableProperty]
    public partial double OffsetY { get; set; }

    public bool HasImage => PreviewImage is not null;
    public bool HasNoImage => _sourceBytes is null;
    public bool HasSourceImage => _sourceBytes is not null;

    public void ResetFraming()
    {
        Zoom = 100;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void AdjustZoom(double delta) => Zoom = Math.Clamp(Zoom + delta, 25, 300);

    public void Pan(double deltaX, double deltaY)
    {
        OffsetX = Math.Clamp(OffsetX + deltaX, -100, 100);
        OffsetY = Math.Clamp(OffsetY + deltaY, -100, 100);
    }

    partial void OnPreviewImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnZoomChanged(double value) => RefreshPreview();
    partial void OnOffsetXChanged(double value) => RefreshPreview();
    partial void OnOffsetYChanged(double value) => RefreshPreview();

    public void SetImage(byte[] bytes, string fileName)
    {
        _updating = true;

        _sourceBytes = bytes;
        FileName = fileName;
        StatusMessage = "";

        Zoom = 100;
        OffsetX = 0;
        OffsetY = 0;

        _updating = false;

        OnPropertyChanged(nameof(HasNoImage));
        OnPropertyChanged(nameof(HasSourceImage));

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_updating || _sourceBytes is null)
            return;

        var cancellation = new CancellationTokenSource();

        var previous = _previewCancellation;
        _previewCancellation = cancellation;

        previous?.Cancel();

        byte[] source = _sourceBytes;
        double zoom = Zoom / 100.0;
        double offsetX = OffsetX;
        double offsetY = OffsetY;

        _ = RefreshPreviewAsync(
            source, zoom, offsetX, offsetY, cancellation);
    }

    private async Task RefreshPreviewAsync(
        byte[] source,
        double zoom,
        double offsetX,
        double offsetY,
        CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;

        try
        {
            await Task.Delay(50, token);

            byte[] previewBytes = await Task.Run(
                () => ReversalRenderer.RenderPreview(
                    source, zoom, offsetX, offsetY, token),
                token);

            token.ThrowIfCancellationRequested();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested ||
                    !ReferenceEquals(_previewCancellation, cancellation))
                    return;

                using var stream = new MemoryStream(previewBytes);
                PreviewImage = new Bitmap(stream);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested &&
                    ReferenceEquals(_previewCancellation, cancellation))
                {
                    StatusMessage = "Erro ao atualizar a prévia.";
                }
            });
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
                _previewCancellation = null;

            cancellation.Dispose();
        }
    }

    public Task<byte[]> RenderExportAsync(int target, int supersample)
    {
        if (_sourceBytes is null)
            throw new InvalidOperationException("Nenhuma imagem carregada.");

        byte[] source = _sourceBytes;
        double zoom = Zoom / 100.0;
        double offsetX = OffsetX;
        double offsetY = OffsetY;

        return Task.Run(() => ReversalRenderer.RenderIconPng(
            source, target, zoom, offsetX, offsetY, supersample));
    }

    public void Dispose()
    {
        var cancellation = _previewCancellation;
        _previewCancellation = null;

        cancellation?.Cancel();

        _sourceBytes = null;
        PreviewImage = null;

        OnPropertyChanged(nameof(HasNoImage));
        OnPropertyChanged(nameof(HasSourceImage));
    }
}
