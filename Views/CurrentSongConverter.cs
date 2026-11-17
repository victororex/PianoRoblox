using System.Globalization;
using System.Windows.Data;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Views;

public sealed class CurrentSongConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is Song song && values[1] is Song current && song.Id == current.Id;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
