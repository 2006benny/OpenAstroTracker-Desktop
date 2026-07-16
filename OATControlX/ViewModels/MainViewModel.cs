using Avalonia.Threading;
using OATCommunications;
using OATCommunications.CommunicationHandlers;
using OATCommunications.CrossPlatform;
using OATCommunications.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace OATControlX.ViewModels
{
	public class MainViewModel : ViewModelBase
	{
		private static readonly CultureInfo Oat = CultureInfo.InvariantCulture;

		private ICommunicationHandler _handler;
		private OatmealTelescopeCommandHandlers _oat;
		private DispatcherTimer _pollTimer;
		private bool _pollBusy;
		private int _pollTick;

		public MainViewModel()
		{
			CommunicationHandlerFactory.Initialize();

			RescanCommand = new RelayCommand(RescanDevices);
			ConnectCommand = new RelayCommand(async () => await OnConnectOrDisconnect());
			StopAllCommand = new RelayCommand(() => { Send(":Q#"); Send(":Qq#"); AppendConsole("< STOP (:Q# :Qq#)"); });
			GoHomeCommand = new RelayCommand(() => Send(":hF#"), () => IsConnected);
			SetHomeCommand = new RelayCommand(() => SendConfirm(":SHP#"), () => IsConnected);
			ParkCommand = new RelayCommand(() => Send(":hP#"), () => IsConnected);
			AutoHomeRACommand = new RelayCommand(() => SendConfirm($":MHR{RAHomeDirection}2#"), () => IsConnected && HasRAAutoHome);
			AutoHomeDECCommand = new RelayCommand(() => SendConfirm($":MHD{DECHomeDirection}2#"), () => IsConnected && HasDECAutoHome);
			ReadHomeOffsetsCommand = new RelayCommand(ReadHomeOffsets, () => IsConnected);
			WriteRAHomeOffsetCommand = new RelayCommand(() => { Send($":XSHR{RAHomeOffset}#"); AppendConsole($"< RA home offset = {RAHomeOffset}"); }, () => IsConnected);
			WriteDECHomeOffsetCommand = new RelayCommand(() => { Send($":XSHD{DECHomeOffset}#"); AppendConsole($"< DEC home offset = {DECHomeOffset}"); }, () => IsConnected);
			RefreshCalibrationCommand = new RelayCommand(RefreshCalibration, () => IsConnected);
			ApplyCalibrationCommand = new RelayCommand(ApplyCalibration, () => IsConnected);
			FactoryResetCommand = new RelayCommand(async () => await OnFactoryReset(), () => IsConnected);
			SendConsoleCommand = new RelayCommand(OnSendConsoleCommand, () => IsConnected);
			NudgeCommand = new RelayCommand(p => OnNudge(p as string), _ => IsConnected);
			ToggleThemeCommand = new RelayCommand(() =>
			{
				ThemeManager.Toggle();
				OnPropertyChanged(nameof(IsRedMode));
			});

			SelectedBaudRate = AppConfig.Current.BaudRate;
			TcpTarget = AppConfig.Current.TcpTarget;
			RescanDevices();
			if (!string.IsNullOrEmpty(AppConfig.Current.LastDevice) && Devices.Contains(AppConfig.Current.LastDevice))
			{
				SelectedDevice = AppConfig.Current.LastDevice;
			}
		}

		// ------------------------------------------------------------------
		// Commands
		// ------------------------------------------------------------------
		public RelayCommand RescanCommand { get; }
		public RelayCommand ConnectCommand { get; }
		public RelayCommand StopAllCommand { get; }
		public RelayCommand GoHomeCommand { get; }
		public RelayCommand SetHomeCommand { get; }
		public RelayCommand ParkCommand { get; }
		public RelayCommand AutoHomeRACommand { get; }
		public RelayCommand AutoHomeDECCommand { get; }
		public RelayCommand ReadHomeOffsetsCommand { get; }
		public RelayCommand WriteRAHomeOffsetCommand { get; }
		public RelayCommand WriteDECHomeOffsetCommand { get; }
		public RelayCommand RefreshCalibrationCommand { get; }
		public RelayCommand ApplyCalibrationCommand { get; }
		public RelayCommand FactoryResetCommand { get; }
		public RelayCommand SendConsoleCommand { get; }
		public RelayCommand NudgeCommand { get; }
		public RelayCommand ToggleThemeCommand { get; }

		public bool IsRedMode => ThemeManager.CurrentTheme == AppTheme.Red;

		// ------------------------------------------------------------------
		// Connection
		// ------------------------------------------------------------------
		public ObservableCollection<string> Devices { get; } = new ObservableCollection<string>();

		private string _selectedDevice;
		public string SelectedDevice { get => _selectedDevice; set => SetProperty(ref _selectedDevice, value); }

		public int[] BaudRates { get; } = { 9600, 19200, 38400, 57600, 115200, 230400 };

		private int _selectedBaudRate = 19200;
		public int SelectedBaudRate { get => _selectedBaudRate; set => SetProperty(ref _selectedBaudRate, value); }

		private string _tcpTarget = string.Empty;
		public string TcpTarget { get => _tcpTarget; set => SetProperty(ref _tcpTarget, value); }

		private bool _isConnected;
		public bool IsConnected
		{
			get => _isConnected;
			set
			{
				if (SetProperty(ref _isConnected, value))
				{
					OnPropertyChanged(nameof(ConnectButtonText));
					RefreshCommandStates();
				}
			}
		}

		private string _connectionStatus = "Non connecté";
		public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }

		public string ConnectButtonText => IsConnected ? "Déconnecter" : "Connecter";

		private void RefreshCommandStates()
		{
			foreach (var cmd in new[] { GoHomeCommand, SetHomeCommand, ParkCommand, AutoHomeRACommand, AutoHomeDECCommand,
				ReadHomeOffsetsCommand, WriteRAHomeOffsetCommand, WriteDECHomeOffsetCommand, RefreshCalibrationCommand,
				ApplyCalibrationCommand, FactoryResetCommand, SendConsoleCommand, NudgeCommand })
			{
				cmd.RaiseCanExecuteChanged();
			}
		}

		private void RescanDevices()
		{
			Devices.Clear();
			CommunicationHandlerFactory.DiscoverDevices(device => Dispatcher.UIThread.Post(() =>
			{
				if (!Devices.Contains(device))
				{
					Devices.Add(device);
				}
			}));
			if (Devices.Any() && SelectedDevice == null)
			{
				SelectedDevice = Devices.First();
			}
		}

		private async Task OnConnectOrDisconnect()
		{
			if (IsConnected)
			{
				Disconnect();
				return;
			}

			string device = SelectedDevice;
			ICommunicationHandler handler = null;

			if (!string.IsNullOrWhiteSpace(TcpTarget))
			{
				var parts = TcpTarget.Split(':');
				if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var ip) && int.TryParse(parts[1], out var port))
				{
					handler = new TcpCommunicationHandler(ip, port);
					device = $"WiFi {TcpTarget}";
				}
				else
				{
					ConnectionStatus = "Adresse TCP invalide (attendu ip:port)";
					return;
				}
			}
			else if (!string.IsNullOrEmpty(device))
			{
				string spec = device.StartsWith("Serial: ") ? $"{device}@{SelectedBaudRate}" : device;
				handler = CommunicationHandlerFactory.ConnectToDevice(spec);
			}

			if (handler == null)
			{
				ConnectionStatus = "Aucun périphérique sélectionné";
				return;
			}

			ConnectionStatus = $"Connexion à {device}...";
			bool opened = await Task.Run(() => handler.Connect());
			if (!opened)
			{
				ConnectionStatus = $"Échec d'ouverture de {device}";
				return;
			}

			_handler = handler;
			_oat = new OatmealTelescopeCommandHandlers(handler);

			if (handler is SerialCommunicationHandler)
			{
				// Opening the port toggles DTR/RTS, which resets ESP32-based
				// boards; give the firmware time to boot before talking to it.
				ConnectionStatus = "Port ouvert — attente du démarrage de la carte (3 s)...";
				await Task.Delay(3000);
			}

			bool ok = await RunHandshake();
			if (!ok)
			{
				ConnectionStatus = "Le périphérique ne répond pas comme un OAT/OAE (vérifie le baud rate)";
				_handler.Disconnect();
				_handler = null;
				_oat = null;
				return;
			}

			AppConfig.Current.LastDevice = SelectedDevice ?? string.Empty;
			AppConfig.Current.BaudRate = SelectedBaudRate;
			AppConfig.Current.TcpTarget = TcpTarget ?? string.Empty;
			AppConfig.Save();

			IsConnected = true;
			ConnectionStatus = $"Connecté : {MountName}";

			_pollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000), DispatcherPriority.Normal, async (s, e) => await PollStatus());
			_pollTimer.Start();
		}

		public void Disconnect()
		{
			_pollTimer?.Stop();
			_pollTimer = null;
			try
			{
				_handler?.Disconnect();
			}
			catch (Exception ex)
			{
				Log.WriteLine("MAIN: Exception during disconnect: {0}", ex.Message);
			}
			_handler = null;
			_oat = null;
			IsConnected = false;
			ConnectionStatus = "Non connecté";
			MountStatusText = "--";
		}

		public void Shutdown()
		{
			if (IsConnected)
			{
				Disconnect();
			}
		}

		// ------------------------------------------------------------------
		// Handshake / hardware info
		// ------------------------------------------------------------------
		private string _mountName = "--";
		public string MountName { get => _mountName; set => SetProperty(ref _mountName, value); }

		private string _firmwareText = "--";
		public string FirmwareText { get => _firmwareText; set => SetProperty(ref _firmwareText, value); }

		private long _firmwareVersion;

		private string _boardText = "--";
		public string BoardText { get => _boardText; set => SetProperty(ref _boardText, value); }

		private string _raStepperText = "--";
		public string RAStepperText { get => _raStepperText; set => SetProperty(ref _raStepperText, value); }

		private string _decStepperText = "--";
		public string DECStepperText { get => _decStepperText; set => SetProperty(ref _decStepperText, value); }

		private string _featuresText = "--";
		public string FeaturesText { get => _featuresText; set => SetProperty(ref _featuresText, value); }

		private string _networkText = "n/a";
		public string NetworkText { get => _networkText; set => SetProperty(ref _networkText, value); }

		private string _temperatureText = "n/a";
		public string TemperatureText { get => _temperatureText; set => SetProperty(ref _temperatureText, value); }

		private bool _hasAzAlt;
		public bool HasAzAlt { get => _hasAzAlt; set => SetProperty(ref _hasAzAlt, value); }

		private bool _hasRAAutoHome;
		public bool HasRAAutoHome { get => _hasRAAutoHome; set => SetProperty(ref _hasRAAutoHome, value); }

		private bool _hasDECAutoHome;
		public bool HasDECAutoHome { get => _hasDECAutoHome; set => SetProperty(ref _hasDECAutoHome, value); }

		private bool _hasGyro;
		private bool _hwInfoPending;

		private async Task<string> QueryWithRetry(string command, int attempts = 3)
		{
			for (int i = 0; i < attempts; i++)
			{
				string result = await QueryAsync(command);
				if (!string.IsNullOrEmpty(result))
				{
					return result;
				}
				AppendConsole($"<   {command} : pas de réponse (essai {i + 1}/{attempts})");
				if (i < attempts - 1)
				{
					// The OAE ESP32 firmware answers simple queries right after
					// boot but needs a little longer before :XGM# is served;
					// give it breathing room between attempts.
					await Task.Delay(1000);
				}
			}
			return null;
		}

		private async Task<bool> RunHandshake()
		{
			AppendConsole("< Handshake : interrogation de la monture...");
			string product = await QueryWithRetry(":GVP#");
			if (string.IsNullOrEmpty(product))
			{
				AppendConsole("< ÉCHEC : aucune réponse à :GVP# — mauvais port, mauvais baud, ou carte pas prête");
				return false;
			}
			AppendConsole($"<   :GVP# → {product}");

			string version = await QueryWithRetry(":GVN#");
			if (string.IsNullOrEmpty(version) || version.StartsWith("["))
			{
				AppendConsole($"< ÉCHEC : réponse :GVN# invalide ({version ?? "aucune"})");
				return false;
			}
			AppendConsole($"<   :GVN# → {version}");

			MountName = $"{product} {version}";
			FirmwareText = version;
			var vParts = version.TrimStart('V', 'v').Split('.');
			if (vParts.Length == 3
				&& long.TryParse(vParts[0], out long vMaj)
				&& long.TryParse(vParts[1], out long vMin)
				&& long.TryParse(vParts[2], out long vPatch))
			{
				_firmwareVersion = vMaj * 10000L + vMin * 100L + vPatch;
			}
			_oat.SetFirmwareVersion(_firmwareVersion);

			string hw = await QueryWithRetry(":XGM#");
			if (IsValidHardwareInfo(hw))
			{
				AppendConsole($"<   :XGM# → {hw}");
				ParseHardware(hw);
				AppendConsole($"<   AZ/ALT motorisé : {(HasAzAlt ? "OUI" : "NON")}  |  AutoHome RA : {(HasRAAutoHome ? "OUI" : "NON")}  |  DEC : {(HasDECAutoHome ? "OUI" : "NON")}");
			}
			else
			{
				_hwInfoPending = true;
				AppendConsole($"< ATTENTION : réponse :XGM# absente ou invalide ({hw ?? "aucune"}) — nouvel essai automatique toutes les 5 s");
			}

			if (_firmwareVersion > 10875)
			{
				string steppers = await QueryAsync(":XGMS#");
				if (!string.IsNullOrEmpty(steppers))
				{
					ParseStepperInfo(steppers);
				}
			}

			RefreshCalibration();
			ReadHomeOffsets();

			if (_hasGyro)
			{
				string temp = await QueryAsync(":XLGT#");
				if (!string.IsNullOrEmpty(temp))
				{
					TemperatureText = $"{temp} °C";
				}
			}

			AppendConsole($"< Connecté à {MountName}");
			return true;
		}

		// A real :XGM# reply is "board,RAstepper,DECstepper[,addons...]".
		private static bool IsValidHardwareInfo(string hw)
		{
			return !string.IsNullOrEmpty(hw) && hw.Split(',').Length >= 3;
		}

		private void ParseHardware(string hwData)
		{
			var hwParts = hwData.Split(',');
			BoardText = hwParts[0];
			if (hwParts.Length > 2)
			{
				// Classic OAT: "NEMA 17|400" (type|teeth); OAE ESP32 firmware:
				// "NEMA|16|3600" (type|µstep|steps) — display fields as-is.
				RAStepperText = string.Join(", ", hwParts[1].Split('|'));
				DECStepperText = string.Join(", ", hwParts[2].Split('|'));
			}

			bool hasAz = false, hasAlt = false;
			var features = string.Empty;
			for (int i = 3; i < hwParts.Length; i++)
			{
				switch (hwParts[i])
				{
					case "AUTO_AZ_ALT": features += "AutoPA, "; hasAz = true; hasAlt = true; break;
					case "AUTO_ALT": features += "MotorALT, "; hasAlt = true; break;
					case "AUTO_AZ": features += "MotorAZ, "; hasAz = true; break;
					case "GPS": features += "GPS, "; break;
					case "GYRO": features += "Niveau numérique, "; _hasGyro = true; break;
					case "FOC": features += "Focuser, "; break;
					case "HSAH": features += "RA AutoHome, "; HasRAAutoHome = true; break;
					case "HSAV": features += "DEC AutoHome, "; HasDECAutoHome = true; break;
					default:
						// OAE firmware lists absent addons as NO_GPS, NO_GYRO, ...
						if (!hwParts[i].StartsWith("NO_"))
						{
							features += hwParts[i] + ", ";
						}
						break;
				}
			}
			HasAzAlt = hasAz || hasAlt;
			FeaturesText = string.IsNullOrEmpty(features) ? "Aucun addon" : features.TrimEnd(' ', ',');
		}

		private void ParseStepperInfo(string stepperData)
		{
			string DriverName(string code)
			{
				switch (code)
				{
					case "U": return "ULN2003";
					case "TU": return "TMC2209 UART";
					case "TS": return "TMC2209 Standalone";
					case "A": return "A4988 Generic";
					default: return code;
				}
			}

			var steppers = stepperData.Split('|');
			if (steppers.Length > 1)
			{
				var ra = steppers[0].Split(',');
				var dec = steppers[1].Split(',');
				RAStepperText += $"  [{DriverName(ra[0])}]";
				DECStepperText += $"  [{DriverName(dec[0])}]";
			}
		}

		// ------------------------------------------------------------------
		// Status polling (:GX#)
		// ------------------------------------------------------------------
		private string _mountStatusText = "--";
		public string MountStatusText { get => _mountStatusText; set => SetProperty(ref _mountStatusText, value); }

		private string _currentRAText = "--h --m --s";
		public string CurrentRAText { get => _currentRAText; set => SetProperty(ref _currentRAText, value); }

		private string _currentDECText = "--° --' --\"";
		public string CurrentDECText { get => _currentDECText; set => SetProperty(ref _currentDECText, value); }

		private string _stepperText = "--";
		public string StepperText { get => _stepperText; set => SetProperty(ref _stepperText, value); }

		private bool _raMotorActive;
		public bool RAMotorActive { get => _raMotorActive; set => SetProperty(ref _raMotorActive, value); }

		private bool _decMotorActive;
		public bool DECMotorActive { get => _decMotorActive; set => SetProperty(ref _decMotorActive, value); }

		private bool _trkMotorActive;
		public bool TrkMotorActive { get => _trkMotorActive; set => SetProperty(ref _trkMotorActive, value); }

		private bool _isTracking;
		public bool IsTracking
		{
			get => _isTracking;
			set
			{
				if (SetProperty(ref _isTracking, value) && IsConnected && !_updatingFromPoll)
				{
					SendConfirm($":MT{(value ? 1 : 0)}#");
					AppendConsole($"< Suivi {(value ? "activé" : "désactivé")}");
				}
			}
		}

		private bool _updatingFromPoll;

		private async Task PollStatus()
		{
			if (_pollBusy || _oat == null)
			{
				return;
			}
			_pollBusy = true;
			try
			{
				string status = await QueryAsync(":GX#");
				if (!string.IsNullOrWhiteSpace(status))
				{
					ParseStatus(status);
				}

				_pollTick++;
				if (_hwInfoPending && (_pollTick % 5) == 0)
				{
					string hw = await QueryAsync(":XGM#");
					if (IsValidHardwareInfo(hw))
					{
						_hwInfoPending = false;
						AppendConsole($"<   :XGM# → {hw}");
						ParseHardware(hw);
						AppendConsole($"<   AZ/ALT motorisé : {(HasAzAlt ? "OUI" : "NON")}  |  AutoHome RA : {(HasRAAutoHome ? "OUI" : "NON")}  |  DEC : {(HasDECAutoHome ? "OUI" : "NON")}");
					}
				}
				if (_hasGyro && (_pollTick % 30) == 0)
				{
					string temp = await QueryAsync(":XLGT#");
					if (!string.IsNullOrEmpty(temp))
					{
						TemperatureText = $"{temp} °C";
					}
				}
			}
			catch (Exception ex)
			{
				Log.WriteLine("MAIN: Poll exception: {0}", ex.Message);
			}
			finally
			{
				_pollBusy = false;
			}
		}

		// :GX# reply: Idle,--T--,12345,6789,101112,080300,+900000[,focus]
		//   [0] status  [1] motion flags (RA,DEC,TRK,AZ,ALT,FOC)  [2..4] stepper positions
		//   [5] RA HHMMSS  [6] DEC sDDMMSS
		private void ParseStatus(string status)
		{
			var parts = status.Split(',');
			if (parts.Length < 7)
			{
				return;
			}

			MountStatusText = parts[0];

			var flags = parts[1];
			RAMotorActive = flags.Length > 0 && flags[0] != '-';
			DECMotorActive = flags.Length > 1 && flags[1] != '-';
			TrkMotorActive = flags.Length > 2 && flags[2] == 'T';

			_updatingFromPoll = true;
			try
			{
				bool tracking = flags.Length > 2 && flags[2] == 'T';
				if (_isTracking != tracking)
				{
					_isTracking = tracking;
					OnPropertyChanged(nameof(IsTracking));
				}
			}
			finally
			{
				_updatingFromPoll = false;
			}

			StepperText = $"RA {parts[2]}  DEC {parts[3]}  TRK {parts[4]}";

			if (parts[5].Length >= 6)
			{
				CurrentRAText = $"{parts[5].Substring(0, 2)}h {parts[5].Substring(2, 2)}m {parts[5].Substring(4, 2)}s";
			}
			if (parts[6].Length >= 7)
			{
				CurrentDECText = $"{parts[6].Substring(0, 3)}° {parts[6].Substring(3, 2)}' {parts[6].Substring(5, 2)}\"";
			}
		}

		// ------------------------------------------------------------------
		// Manual motion
		// ------------------------------------------------------------------
		private int _slewRate = 3;
		public int SlewRate
		{
			get => _slewRate;
			set
			{
				if (SetProperty(ref _slewRate, value) && IsConnected)
				{
					// LX200 rates: Guide, Centering, Move, Slew(max)
					char c = "GCMS"[Math.Clamp(value, 1, 4) - 1];
					Send($":R{c}#");
					AppendConsole($"< Vitesse de déplacement : {value} (:R{c}#)");
				}
			}
		}

		public void StartMove(string direction)
		{
			if (IsConnected)
			{
				Send($":M{direction}#");
			}
		}

		public void StopMove(string direction)
		{
			if (IsConnected)
			{
				Send($":Q{direction}#");
			}
		}

		private double _nudgeArcmin = 1.0;
		public double NudgeArcmin { get => _nudgeArcmin; set => SetProperty(ref _nudgeArcmin, value); }
		public double[] NudgeSteps { get; } = { 0.5, 1.0, 2.0, 5.0 };

		private void OnNudge(string axisAndSign)
		{
			if (string.IsNullOrEmpty(axisAndSign))
			{
				return;
			}
			// parameter: "AZ:+", "AZ:-", "ALT:+", "ALT:-"
			var parts = axisAndSign.Split(':');
			double amount = NudgeArcmin * (parts[1] == "-" ? -1 : 1);
			string cmd = parts[0] == "AZ"
				? string.Format(Oat, ":MAZ{0:0.0}#", amount)
				: string.Format(Oat, ":MAL{0:0.0}#", amount);
			Send(cmd);
			AppendConsole($"< {parts[0]} {amount:+0.0;-0.0}' ({cmd})");
		}

		// ------------------------------------------------------------------
		// Homing
		// ------------------------------------------------------------------
		public string[] RAHomeDirections { get; } = { "R", "L" };
		public string[] DECHomeDirections { get; } = { "U", "D" };

		private string _raHomeDirection = "R";
		public string RAHomeDirection { get => _raHomeDirection; set => SetProperty(ref _raHomeDirection, value); }

		private string _decHomeDirection = "U";
		public string DECHomeDirection { get => _decHomeDirection; set => SetProperty(ref _decHomeDirection, value); }

		private long _raHomeOffset;
		public long RAHomeOffset { get => _raHomeOffset; set => SetProperty(ref _raHomeOffset, value); }

		private long _decHomeOffset;
		public long DECHomeOffset { get => _decHomeOffset; set => SetProperty(ref _decHomeOffset, value); }

		private async void ReadHomeOffsets()
		{
			if (HasRAAutoHome)
			{
				string ra = await QueryAsync(":XGHS#");
				if (long.TryParse(ra, out long raOff))
				{
					RAHomeOffset = raOff;
				}
			}
			if (HasDECAutoHome)
			{
				string dec = await QueryAsync(":XGHD#");
				if (long.TryParse(dec, out long decOff))
				{
					DECHomeOffset = decOff;
				}
			}
		}

		// ------------------------------------------------------------------
		// Calibration
		// ------------------------------------------------------------------
		private double _raStepsPerDeg;
		public double RAStepsPerDeg { get => _raStepsPerDeg; set => SetProperty(ref _raStepsPerDeg, value); }

		private double _decStepsPerDeg;
		public double DECStepsPerDeg { get => _decStepsPerDeg; set => SetProperty(ref _decStepsPerDeg, value); }

		private double _speedFactor;
		public double SpeedFactor { get => _speedFactor; set => SetProperty(ref _speedFactor, value); }

		private async void RefreshCalibration()
		{
			string ra = await QueryAsync(":XGR#");
			if (double.TryParse(ra, NumberStyles.Float, Oat, out double raSteps))
			{
				RAStepsPerDeg = raSteps;
			}
			string dec = await QueryAsync(":XGD#");
			if (double.TryParse(dec, NumberStyles.Float, Oat, out double decSteps))
			{
				DECStepsPerDeg = decSteps;
			}
			string speed = await QueryAsync(":XGS#");
			if (double.TryParse(speed, NumberStyles.Float, Oat, out double speedFactor))
			{
				SpeedFactor = speedFactor;
			}
		}

		private void ApplyCalibration()
		{
			Send(string.Format(Oat, ":XSR{0:0.0}#", RAStepsPerDeg));
			Send(string.Format(Oat, ":XSD{0:0.0}#", DECStepsPerDeg));
			Send(string.Format(Oat, ":XSS{0:0.0000}#", SpeedFactor));
			AppendConsole($"< Calibration appliquée : RA {RAStepsPerDeg.ToString("0.0", Oat)} pas/°, DEC {DECStepsPerDeg.ToString("0.0", Oat)} pas/°, facteur {SpeedFactor.ToString("0.0000", Oat)}");
		}

		// ------------------------------------------------------------------
		// Maintenance
		// ------------------------------------------------------------------
		private bool _confirmingReset;
		public bool ConfirmingReset { get => _confirmingReset; set => SetProperty(ref _confirmingReset, value); }

		public string ResetButtonText => ConfirmingReset ? "Confirmer le reset EEPROM !" : "Reset EEPROM (usine)";

		private async Task OnFactoryReset()
		{
			if (!ConfirmingReset)
			{
				ConfirmingReset = true;
				OnPropertyChanged(nameof(ResetButtonText));
				_ = Task.Delay(5000).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
				{
					ConfirmingReset = false;
					OnPropertyChanged(nameof(ResetButtonText));
				}));
				return;
			}

			ConfirmingReset = false;
			OnPropertyChanged(nameof(ResetButtonText));
			SendConfirm(":XFR#");
			AppendConsole("< Reset EEPROM envoyé (:XFR#). Redémarre la monture.");
			await Task.CompletedTask;
		}

		// ------------------------------------------------------------------
		// Console
		// ------------------------------------------------------------------
		public ObservableCollection<string> ConsoleLines { get; } = new ObservableCollection<string>();

		private string _consoleInput = string.Empty;
		public string ConsoleInput { get => _consoleInput; set => SetProperty(ref _consoleInput, value); }

		public string[] ResponseTypes { get; } = { "Réponse #", "Aucune réponse", "Chiffre", "Double #" };

		private int _selectedResponseType;
		public int SelectedResponseType { get => _selectedResponseType; set => SetProperty(ref _selectedResponseType, value); }

		private void OnSendConsoleCommand()
		{
			var cmd = (ConsoleInput ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(cmd) || _oat == null)
			{
				return;
			}
			if (!cmd.StartsWith(":"))
			{
				cmd = ":" + cmd;
			}
			if (!cmd.EndsWith("#"))
			{
				cmd += "#";
			}

			AppendConsole($"> {cmd}");
			Action<CommandResponse> onDone = r => Dispatcher.UIThread.Post(() =>
				AppendConsole(r.Success ? $"  {r.Data}" : $"  ÉCHEC : {r.StatusMessage}"));

			switch (SelectedResponseType)
			{
				case 1: _handler.SendBlind(cmd, onDone); break;
				case 2: _handler.SendCommandConfirm(cmd, onDone); break;
				case 3: _handler.SendCommandDoubleResponse(cmd, onDone); break;
				default: _handler.SendCommand(cmd, onDone); break;
			}
			ConsoleInput = string.Empty;
		}

		private void AppendConsole(string line)
		{
			ConsoleLines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
			while (ConsoleLines.Count > 500)
			{
				ConsoleLines.RemoveAt(0);
			}
		}

		// ------------------------------------------------------------------
		// Low-level helpers
		// ------------------------------------------------------------------
		private void Send(string command)
		{
			_handler?.SendBlind(command, _ => { });
		}

		private void SendConfirm(string command)
		{
			_handler?.SendCommandConfirm(command, _ => { });
		}

		private Task<string> QueryAsync(string command)
		{
			var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (_handler == null)
			{
				tcs.SetResult(null);
			}
			else
			{
				// The legacy TCP handler fabricates a successful "0#" reply on
				// read timeout; treat it as the failure it really is.
				_handler.SendCommand(command, r => tcs.TrySetResult(r.Success && r.Data != "0#" ? r.Data : null));
			}
			return tcs.Task;
		}
	}
}
