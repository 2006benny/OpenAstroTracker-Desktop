using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OATControlX.ViewModels;
using System.Collections.Specialized;

namespace OATControlX.Views
{
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();

			// Slew buttons are press-and-hold: :Mx# on press, :Qx# on release.
			WireHoldButton(BtnNorth, "n");
			WireHoldButton(BtnSouth, "s");
			WireHoldButton(BtnEast, "e");
			WireHoldButton(BtnWest, "w");

			ConsoleInputBox.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && Vm != null && Vm.SendConsoleCommand.CanExecute(null))
				{
					// Force the pending text into the binding before sending.
					ConsoleInputBox.Focus();
					Vm.ConsoleInput = ConsoleInputBox.Text;
					Vm.SendConsoleCommand.Execute(null);
					e.Handled = true;
				}
			};

			DataContextChanged += (s, e) =>
			{
				if (Vm != null)
				{
					Vm.ConsoleLines.CollectionChanged += OnConsoleChanged;
				}
			};
		}

		private MainViewModel Vm => DataContext as MainViewModel;

		private void WireHoldButton(Button button, string direction)
		{
			button.AddHandler(PointerPressedEvent, (s, e) => Vm?.StartMove(direction), RoutingStrategies.Tunnel, true);
			button.AddHandler(PointerReleasedEvent, (s, e) => Vm?.StopMove(direction), RoutingStrategies.Tunnel, true);
			button.PointerCaptureLost += (s, e) => Vm?.StopMove(direction);
		}

		private void OnConsoleChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Add && ConsoleList.ItemCount > 0)
			{
				Dispatcher.UIThread.Post(() => ConsoleList.ScrollIntoView(ConsoleList.ItemCount - 1));
			}
		}
	}
}
