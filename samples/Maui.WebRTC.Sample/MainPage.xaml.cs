namespace Maui.WebRTC.Sample;

public partial class MainPage : ContentPage
{
	PeerConnectionFactory? _factory;
	PeerConnection? _connection;
	PeerConnection? _loopbackPeer;
	LanSignaling? _signaling;
	VideoSource? _source;
	VideoTrack? _videoTrack;
	AudioTrack? _audioTrack;
	CameraCapturer? _camera;
	TestPatternCapturer? _pattern;
	IDispatcherTimer? _statusTimer;
	string _input = string.Empty;

	public MainPage()
	{
		InitializeComponent();
		Loaded += OnPageLoaded;
	}

	// For automated runs: the role and peer come from the launcher instead of the buttons.
	async void OnPageLoaded(object? sender, EventArgs e)
	{
		switch (LaunchOption("role"))
		{
			case "listen":
				await RunAsync(ListenAsync);
				break;
			case "connect":
				await RunAsync(() => ConnectAsync(LaunchOption("peer") ?? "127.0.0.1"));
				break;
		}
	}

	async void OnListenClicked(object? sender, EventArgs e) => await RunAsync(ListenAsync);

	async void OnConnectClicked(object? sender, EventArgs e) =>
		await RunAsync(() => ConnectAsync(HostEntry.Text?.Trim() ?? string.Empty));

	async void OnLoopbackClicked(object? sender, EventArgs e) => await RunAsync(LoopbackAsync);

	async Task RunAsync(Func<Task> action)
	{
		Controls.IsVisible = false;
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			StatusLabel.Text = $"failed: {ex.Message}";
		}
	}

	async Task ListenAsync()
	{
		await StartLocalMediaAsync(microphoneLive: true);
		StatusLabel.Text = $"waiting on port {LanSignaling.Port}...";

		_signaling = await LanSignaling.ListenAsync();
		StartCall();
		await _signaling.RunAsync(_connection!);
		StatusLabel.Text = "the other side disconnected";
	}

	async Task ConnectAsync(string host)
	{
		if (host.Length == 0)
			throw new InvalidOperationException("Enter the other device's IP address.");

		await StartLocalMediaAsync(microphoneLive: true);
		StatusLabel.Text = $"connecting to {host}...";

		_signaling = await LanSignaling.ConnectAsync(host);
		StartCall();

		var offer = await _connection!.CreateOfferAsync();
		// Sent first so that our candidates, which start with SetLocalDescription, arrive after it.
		await _signaling.SendAsync(offer.Type, offer.Sdp);
		await _connection.SetLocalDescriptionAsync(offer);

		await _signaling.RunAsync(_connection);
		StatusLabel.Text = "the other side disconnected";
	}

	void StartCall()
	{
		_connection = _factory!.CreatePeerConnection();
		_connection.IceCandidateGathered += c => _ = _signaling!.SendAsync("candidate", candidate: c);
		ShowRemoteTracksOf(_connection);
		AddLocalTracksTo(_connection);
		StartStatusUpdates();
	}

	async Task LoopbackAsync()
	{
		// Both peers live on this device, so a live microphone would feed back through the speaker.
		await StartLocalMediaAsync(microphoneLive: false);

		_connection = _factory!.CreatePeerConnection();
		_loopbackPeer = _factory.CreatePeerConnection();
		_connection.IceCandidateGathered += c => _ = _loopbackPeer.AddIceCandidateAsync(c);
		_loopbackPeer.IceCandidateGathered += c => _ = _connection.AddIceCandidateAsync(c);
		ShowRemoteTracksOf(_loopbackPeer);
		AddLocalTracksTo(_connection);
		StartStatusUpdates();

		var offer = await _connection.CreateOfferAsync();
		await _loopbackPeer.SetRemoteDescriptionAsync(offer);
		await _connection.SetLocalDescriptionAsync(offer);
		var answer = await _loopbackPeer.CreateAnswerAsync();
		await _connection.SetRemoteDescriptionAsync(answer);
		await _loopbackPeer.SetLocalDescriptionAsync(answer);
	}

	async Task StartLocalMediaAsync(bool microphoneLive)
	{
		var cameraAllowed = await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted;
		var micAllowed = await Permissions.RequestAsync<Permissions.Microphone>() == PermissionStatus.Granted;

		UseCallAudioRouting();

		_factory = new PeerConnectionFactory();
		_source = _factory.CreateVideoSource();
		_videoTrack = _factory.CreateVideoTrack(_source);
		LocalVideo.Track = _videoTrack;

		if (LaunchOption("testPattern") == "true" || !cameraAllowed || CameraCapturer.DeviceCount == 0)
		{
			_pattern = new TestPatternCapturer(_source);
			_input = "test pattern";
		}
		else
		{
			_camera = new CameraCapturer();
			_camera.Start(_source);
			_input = "camera";
		}

		if (micAllowed)
		{
			_audioTrack = _factory.CreateAudioTrack();
			_audioTrack.Enabled = microphoneLive;
		}
	}

	static void UseCallAudioRouting()
	{
#if ANDROID
		// Without this mode Android applies no echo cancellation and keeps the output at media volume.
		var context = global::Android.App.Application.Context;
		var audio = (global::Android.Media.AudioManager)context.GetSystemService(global::Android.Content.Context.AudioService)!;
		audio.Mode = global::Android.Media.Mode.InCommunication;
#endif
	}

	void AddLocalTracksTo(PeerConnection connection)
	{
		connection.AddTrack(_videoTrack!);
		if (_audioTrack is not null)
			connection.AddTrack(_audioTrack);
	}

	void ShowRemoteTracksOf(PeerConnection connection)
	{
		connection.TrackReceived += track =>
		{
			if (track is VideoTrack video)
				Dispatcher.Dispatch(() => RemoteVideo.Track = video);
		};
	}

	void StartStatusUpdates()
	{
		_statusTimer = Dispatcher.CreateTimer();
		_statusTimer.Interval = TimeSpan.FromSeconds(1);
		_statusTimer.Tick += async (_, _) =>
		{
			var stats = CallStats.Summarize(await _connection!.GetStatsJsonAsync());
			StatusLabel.Text = $"{_input} | {stats}";
			Console.WriteLine($"MRTC-STATUS {StatusLabel.Text}");
		};
		_statusTimer.Start();
	}

	static string? LaunchOption(string name)
	{
#if ANDROID
		return Platform.CurrentActivity?.Intent?.GetStringExtra(name);
#else
		return Environment.GetEnvironmentVariable($"MRTC_{name.ToUpperInvariant()}");
#endif
	}
}
