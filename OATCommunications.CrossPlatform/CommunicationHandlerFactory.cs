using OATCommunications.CommunicationHandlers;
using OATCommunications.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OATCommunications.CrossPlatform
{
	// UI-agnostic replacement for the WPF CommunicationHandlerFactory: no
	// Dispatcher dependency, discovery results are delivered through a plain
	// callback that the caller marshals to its UI thread if needed.
	public static class CommunicationHandlerFactory
	{
		private static readonly List<ICommunicationHandler> _handlers = new List<ICommunicationHandler>();

		public static void Initialize()
		{
			_handlers.Clear();
			_handlers.Add(new SerialCommunicationHandler());
			_handlers.Add(new TcpCommunicationHandler());
		}

		public static void AddHandler(ICommunicationHandler handler)
		{
			_handlers.Add(handler);
		}

		public static IList<ICommunicationHandler> AvailableHandlers => _handlers;

		public static void DiscoverDevices(Action<string> onDeviceFound)
		{
			Log.WriteLine("COMMFACTORY: Device Discovery initiated.");
			foreach (var handler in _handlers)
			{
				handler.DiscoverDeviceInstances((device) =>
				{
					Log.WriteLine("COMMFACTORY: Device found: " + device);
					onDeviceFound(device);
				});
			}
		}

		public static ICommunicationHandler ConnectToDevice(string device)
		{
			Log.WriteLine($"COMMFACTORY: Attempting to connect to device {device}...");
			if (string.IsNullOrEmpty(device))
			{
				return null;
			}

			var useHandler = _handlers.FirstOrDefault(handler => handler.IsDriverForDevice(device));
			if (useHandler == null)
			{
				Log.WriteLine($"COMMFACTORY: No handler for device {device}.");
				return null;
			}

			return useHandler.CreateHandler(device);
		}
	}
}
