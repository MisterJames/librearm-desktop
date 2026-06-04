namespace LibreArm_Desktop.Services;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;

public sealed class ProfilePhotoService
{
    private const uint CroppedSize = 256;

    public async Task<string?> PickAndCropAsync(Window owner, XamlRoot xamlRoot, long profileId)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));

        var sourceFile = await picker.PickSingleFileAsync();
        if (sourceFile is null)
        {
            return null;
        }

        if (!await ConfirmPreviewAsync(xamlRoot, sourceFile))
        {
            return null;
        }

        var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("profile-photos", CreationCollisionOption.OpenIfExists);
        var outputFile = await folder.CreateFileAsync($"profile-{profileId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png", CreationCollisionOption.GenerateUniqueName);
        await CropSquareAsync(sourceFile, outputFile);
        return $"profile-photos/{outputFile.Name}";
    }

    private static async Task<bool> ConfirmPreviewAsync(XamlRoot xamlRoot, StorageFile sourceFile)
    {
        var preview = new BitmapImage();
        using (var stream = await sourceFile.OpenReadAsync())
        {
            await preview.SetSourceAsync(stream);
        }

        var image = new Image
        {
            Source = preview,
            Width = 240,
            Height = 240,
            Stretch = Stretch.UniformToFill
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "LibreArm will save a square center crop for your profile photo.",
            TextWrapping = TextWrapping.WrapWholeWords
        });
        panel.Children.Add(new Border
        {
            Width = 240,
            Height = 240,
            CornerRadius = new CornerRadius(120),
            Child = image
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Use this photo?",
            Content = panel,
            PrimaryButtonText = "Use photo",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task CropSquareAsync(StorageFile sourceFile, StorageFile outputFile)
    {
        await Task.Run(() =>
        {
            using var source = DrawingImage.FromFile(sourceFile.Path);
            var side = Math.Min(source.Width, source.Height);
            var sourceBounds = new DrawingRectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
            using var output = new DrawingBitmap((int)CroppedSize, (int)CroppedSize);
            using var graphics = DrawingGraphics.FromImage(output);
            graphics.DrawImage(source, new DrawingRectangle(0, 0, (int)CroppedSize, (int)CroppedSize), sourceBounds, System.Drawing.GraphicsUnit.Pixel);
            output.Save(outputFile.Path, DrawingImageFormat.Png);
        });
    }
}
