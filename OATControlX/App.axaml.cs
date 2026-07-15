using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OATControlX.ViewModels;
using OATControlX.Views;
using OATCommunications.Utilities;

namespace OATControlX
{
	public partial class App : Application
	{
		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			Log.Init("OATControlX");
			AppConfig.Load();
			ThemeManager.Apply(AppConfig.Current.Theme);

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				var vm = new MainViewModel();
				desktop.MainWindow = new MainWindow
				{
					DataContext = vm,
				};
				desktop.ShutdownRequested += (s, e) =>
				{
					vm.Shutdown();
					AppConfig.Save();
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
