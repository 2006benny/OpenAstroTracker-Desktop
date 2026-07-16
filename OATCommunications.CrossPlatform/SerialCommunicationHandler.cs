using OATCommunications.CommunicationHandlers;
using OATCommunications.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace OATCommunications.CrossPlatform
{
	// Cross-platform version of the serial handler. Device strings are
	// "Serial: COM3" / "Serial: COM3@19200" on Windows and
	// "Serial: /dev/ttyUSB0" / "Serial: /dev/ttyUSB0@19200" on Linux/macOS.
	public class SerialCommunicationHandler : CommunicationHandler
	{
		public const int DefaultBaudRate = 19200;

		private static readonly Regex _deviceRegex = new Regex(@"^(?:[A-Za-z ]+:\s*)?(?<port>[^@\s]+)(?:@(?<rate>\d+))?$");

		private string _portName;
		private SerialPort _port;

		public SerialCommunicationHandler()
		{
			_portName = string.Empty;
			_port = null;
		}

		public SerialCommunicationHandler(string device)
		{
			Log.WriteLine($"COMMFACTORY: Creating cross-platform Serial handler for {device} ...");
			var result = _deviceRegex.Match(device.Trim());
			if (result.Success)
			{
				_portName = result.Groups["port"].Value;
				int rate = DefaultBaudRate;
				if (result.Groups["rate"].Success)
				{
					int.TryParse(result.Groups["rate"].Value, out rate);
				}
				_port = new SerialPort(_portName)
				{
					BaudRate = rate,
					DtrEnable = false,
					// The OAE ESP32 firmware can take >2s to answer :XGM# when
					// queried shortly after boot; allow it some slack.
					ReadTimeout = 3000,
					WriteTimeout = 2000,
				};
			}
			else
			{
				Log.WriteLine($"COMMFACTORY: Unable to parse serial device string [{device}]");
			}
		}

		public override string Name => "Serial Port";

		public override bool Connected => _port != null && _port.IsOpen;

		protected override void RunJob(Job job)
		{
			CommandResponse response = null;

			if (_logJobs) Log.WriteLine("SERIAL: {0:0000}: [{1}] Processing Job", job.Number, job.Command);
			if (Connected)
			{
				_port.DiscardInBuffer();
				try
				{
					_port.Write(job.Command);
				}
				catch (Exception ex)
				{
					Log.WriteLine("SERIAL: {0:0000}: [{1}] Failed to send command. {2}", job.Number, job.Command, ex.Message);
					job.OnFulFilled(new CommandResponse(string.Empty, false, $"Unable to write to {_portName}. " + ex.Message));
					return;
				}

				try
				{
					switch (job.ResponseType)
					{
						case ResponseType.NoResponse:
							response = new CommandResponse(string.Empty, true);
							break;

						case ResponseType.DigitResponse:
							response = new CommandResponse(new string((char)_port.ReadChar(), 1), true);
							break;

						case ResponseType.FullResponse:
							response = new CommandResponse(_port.ReadTo("#"), true);
							break;

						case ResponseType.DoubleFullResponse:
							response = new CommandResponse(_port.ReadTo("#"), true);
							_port.ReadTo("#");
							break;
					}
					if (_logJobs) Log.WriteLine("SERIAL: {0:0000}: [{1}] Received response '{2}'", job.Number, job.Command, response.Data);
				}
				catch (Exception ex)
				{
					Log.WriteLine("SERIAL: {0:0000}: [{1}] Failed to receive response. {2}", job.Number, job.Command, ex.Message);
					response = new CommandResponse(string.Empty, false, $"Unable to read response to {job.Command} from {_portName}. {ex.Message}");
				}
			}
			else
			{
				Log.WriteLine("SERIAL: {0:0000}: Port {1} is not open", job.Number, _portName);
				response = new CommandResponse(string.Empty, false, $"Unable to open {_portName}");
			}

			job.OnFulFilled(response);
			job.Succeeded = response.Success;
		}

		public override bool Connect()
		{
			if (_port == null)
			{
				return false;
			}

			if (!_port.IsOpen)
			{
				try
				{
					Log.WriteLine("SERIAL: Port {0} is not open, attempting to open...", _portName);
					_port.Open();
					if (_port.IsOpen)
					{
						StartJobsProcessor();
					}
				}
				catch (Exception ex)
				{
					Log.WriteLine("SERIAL: Failed to open the port. {0}", ex.Message);
				}
			}
			return _port.IsOpen;
		}

		public override void Disconnect()
		{
			StopJobsProcessor();
			if (_port != null && _port.IsOpen)
			{
				Log.WriteLine("SERIAL: Port is open, sending shutdown command [:Qq#]");
				_port.Write(":Qq#");
				Thread.Sleep(10);
				_port.Close();
				_port = null;
				Log.WriteLine("SERIAL: Disconnected...");
			}
		}

		public override void DiscoverDeviceInstances(Action<string> addDevice)
		{
			foreach (var port in EnumeratePorts())
			{
				Log.WriteLine("SERIAL: Found Serial port [{0}]", port);
				addDevice("Serial: " + port);
			}
		}

		// SerialPort.GetPortNames() on Linux lists every registered UART
		// (/dev/ttyS0..31), which buries the one real USB adapter. Prefer the
		// device names hot-plugged hardware actually gets.
		public static IEnumerable<string> EnumeratePorts()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				var usbPorts = new List<string>();
				foreach (var pattern in new[] { "ttyUSB*", "ttyACM*", "ttyAMA*" })
				{
					try
					{
						usbPorts.AddRange(Directory.GetFiles("/dev", pattern));
					}
					catch
					{
					}
				}
				if (usbPorts.Any())
				{
					return usbPorts.OrderBy(p => p);
				}
			}

			try
			{
				return SerialPort.GetPortNames().OrderBy(p => p);
			}
			catch
			{
				return Enumerable.Empty<string>();
			}
		}

		public override bool IsDriverForDevice(string device)
		{
			return device.StartsWith("Serial: ");
		}

		public override ICommunicationHandler CreateHandler(string device)
		{
			return new SerialCommunicationHandler(device);
		}
	}
}
