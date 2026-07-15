using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using System;

namespace OATControlX
{
	public enum AppTheme
	{
		Dark,
		Red,
	}

	// Swaps the palette ResourceDictionary (slot 0 of MergedDictionaries).
	// All named brushes are looked up via DynamicResource, so the entire UI
	// (including Fluent control states, whose keys the palettes override)
	// re-colors instantly.
	public static class ThemeManager
	{
		public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

		public static void Apply(AppTheme theme)
		{
			var app = Application.Current;
			if (app == null)
			{
				return;
			}

			var uri = theme == AppTheme.Red
				? new Uri("avares://OATControlX/Themes/Red.axaml")
				: new Uri("avares://OATControlX/Themes/Dark.axaml");

			var dict = new ResourceInclude(uri) { Source = uri };
			app.Resources.MergedDictionaries[0] = dict;
			CurrentTheme = theme;
			AppConfig.Current.Theme = theme;
		}

		public static void Toggle()
		{
			Apply(CurrentTheme == AppTheme.Dark ? AppTheme.Red : AppTheme.Dark);
		}
	}
}
