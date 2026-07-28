using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace ThrowMe.Converters;

/// <summary>
/// enum 값이 ConverterParameter 와 같으면 true. RadioButton 등 열거형 선택 바인딩용.
/// ConvertBack 은 체크된 라디오만 해당 enum 값을 돌려준다.
/// </summary>
public sealed class EnumMatchToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && parameter != null &&
           string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            try { return Enum.Parse(targetType, parameter.ToString()!); }
            catch { return Binding.DoNothing; }
        }
        return Binding.DoNothing;
    }
}
