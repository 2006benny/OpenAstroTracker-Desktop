using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace OATControlX.Converters
{
	// Binds an int property to a RadioButton group: checked when the value
	// equals the ConverterParameter, and writes the parameter back on check.
	public class IntEqualsConverter : IValueConverter
	{
		public static readonly IntEqualsConverter Instance = new IntEqualsConverter();

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return System.Convert.ToInt32(value, CultureInfo.InvariantCulture)
				== System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool b && b)
			{
				return System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
			}
			return BindingOperations.DoNothing;
		}
	}
}
