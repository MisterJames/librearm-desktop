namespace LibreArm_Desktop.Converters;

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

public sealed class ProfilePhotoSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage(CreatePhotoUri(path));
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Uri CreatePhotoUri(string path)
    {
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(ApplicationData.Current.LocalFolder.Path, path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
        {
            throw new FileNotFoundException("Profile photo was not found.", fullPath);
        }

        return new Uri(fullPath, UriKind.Absolute);
    }
}
