using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OATCommunications.Utilities;

namespace OATControlX
{
	// Persisted app settings; stored next to OATControl's own settings under
	// ~/.config/OpenAstroTracker (ApplicationData maps there on Linux).
	public class AppConfig
	{
		[JsonConverter(typeof(JsonStringEnumConverter))]
		public AppTheme Theme { get; set; } = AppTheme.Dark;
		public string LastDevice { get; set; } = string.Empty;
		public int BaudRate { get; set; } = 19200;
		public string TcpTarget { get; set; } = string.Empty;

		public static AppConfig Current { get; private set; } = new AppConfig();

		private static string ConfigPath => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"OpenAstroTracker",
			"OATControlX.json");

		public static void Load()
		{
			try
			{
				if (File.Exists(ConfigPath))
				{
					Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
				}
			}
			catch (Exception ex)
			{
				Log.WriteLine("CONFIG: Failed to load settings: {0}", ex.Message);
				Current = new AppConfig();
			}
		}

		public static void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
				File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
			}
			catch (Exception ex)
			{
				Log.WriteLine("CONFIG: Failed to save settings: {0}", ex.Message);
			}
		}
	}
}
