using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.CoreAudioApi; 
using Windows.Media.Control; // For AudioSessionControl
using WindowsMediaController ; // For SMTCClient
using Windows.Foundation; // For IAsyncAction
using Windows.Foundation.Collections; // For IVectorView
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using System.Windows.Input; // For ApiInformation


namespace MindtheMusic
{
    public partial class MainWindow : Window
    {
        private MMDeviceEnumerator? enumerator = null;
        private MMDevice? playbackDevice = null;
        private AudioSessionControl? spotifySession;
        private float previousSpotifyVolume = 1.0f;
        private float loweredSpotifyVolume = 0.0f;
        private bool isSpotifyActive = false; // Track if Spotify is active 
        private readonly object playbackDeviceLock = new object(); // Lock for playback device access
        private Task currentMonitorTask = Task.CompletedTask; // Track the current monitor task
        private CancellationTokenSource monitorTokenSource = null;
        private MediaManager mediaManager;
        private bool hasStoredOriginalVolume = false; // Flag to check if original volume is stored
        //private bool usePauseMode = false; // Flag to indicate if pause mode is used
        private enum SpotifyMuteMode { None, Volume, Pause }
        private SpotifyMuteMode currentMuteMode = SpotifyMuteMode.Volume;
        public MainWindow()
        {
            Debug.WriteLine("App starting...");
            InitializeComponent();

            // Initialize devices immediately to check at start up
            enumerator = new MMDeviceEnumerator();
            playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _ = InitializeMediaManagerAsync(); // Initialize MediaManager asynchronously
            // Check Spotify running status right when app loads
            CheckSpotifyRunning();
        }

        private async Task InitializeMediaManagerAsync()
        {
            mediaManager = new MediaManager();
            await mediaManager.StartAsync();
            Debug.WriteLine("MediaManager initialized.");
        }

        private void Play_Pause_Click(object sender, RoutedEventArgs e)
        {
            hasStoredOriginalVolume = false; // Reset the flag when the button is clicked
            currentMuteMode = SpotifyMuteMode.Pause;
            Debug.WriteLine("[Button] Play/Pause button clicked.");
            StatusTextBlock.Text = "Spotify paused. Waiting to resume.";
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[UI] Start Monitoring button clicked.");
            StartMonitoring();
        }

        private void PauseSpotify()
        {
            Debug.WriteLine("[Control] Attempting to pause Spotify...");
            foreach (var session in mediaManager.CurrentMediaSessions.Values)
            {
                if (session.Id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    session.ControlSession.TryPauseAsync();
                    Debug.WriteLine($"[Control] Checking session: {session.Id}");
                    return;
                }
            }
        }
        private void ResumeSpotify()
        {
            Debug.WriteLine("[Control] Attempting to resume Spotify...");
            foreach (var session in mediaManager.CurrentMediaSessions.Values)
            {
                if (session.Id.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    session.ControlSession.TryPlayAsync();
                    return;
                }
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            loweredSpotifyVolume = (float)e.NewValue / 100f;
            Debug.WriteLine($"[Slider] Value changed to {loweredSpotifyVolume:F2}");

            // Automatically switched out of pause mode if user moves the slider
            if (currentMuteMode != SpotifyMuteMode.Pause)
            {
                currentMuteMode = SpotifyMuteMode.Volume;
                Debug.WriteLine("[Slider] User moved slider -- Switching out of Pause Mode.");
            }

            Debug.WriteLine($"[Slider] value changed to {loweredSpotifyVolume}");
            Debug.WriteLine("[Slider] Stored new lowered volume value. Will apply during video playback.");
        }
        private void CheckSpotifyRunning()
        {
            Debug.WriteLine("Checking if Spotify is running...");
            FindSpotifySession();

            if (spotifySession == null)
            {
                StatusTextBlock.Text = "Spotify not running! Please start Spotify.";
                Debug.WriteLine("Spotify not running! Please start Spotify.");
            }
            else
            {
                StatusTextBlock.Text = "Spotify detected! Ready to monitor.";
                Debug.WriteLine("Spotify detected! Ready to monitor.");

                StartMonitoring(); // Automatically start monitoring if Spotify is found
            }
        }

        private void VolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (currentMuteMode == SpotifyMuteMode.Pause)
            {
                currentMuteMode = SpotifyMuteMode.Volume;
                Debug.WriteLine("[Slider] User moved slider -- Switching to volume mode.");
                StatusTextBlock.Text = "Mode switched to volume reduction.";
            }
        }
        private async void StartMonitoring()
        {
            Debug.WriteLine("[Monitor] Starting monitoring...");
            // Cancel previous token source and wait for task completion
            if (monitorTokenSource != null)
            {
                monitorTokenSource.Cancel();
                try { await currentMonitorTask; } catch { }
                monitorTokenSource.Dispose();
                Debug.WriteLine("[Monitor] Cancelling previous monitor...");
                monitorTokenSource.Cancel();
            }

            monitorTokenSource = new CancellationTokenSource();

            lock (playbackDeviceLock)
            {
                Debug.WriteLine("[Monitor] Refreshing audio devices...");
                playbackDevice?.Dispose(); // Dispose old device
                enumerator?.Dispose();     // Dispose old enumerator

                enumerator = new MMDeviceEnumerator();
                playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            FindSpotifySession();

            if (spotifySession == null)
            {
                StatusTextBlock.Text = "Spotify not found! Please start Spotify before monitoring!";
                Debug.WriteLine("Spotify not found! Please start Spotify before monitoring!");
                return;
            }

            Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify found. Waiting for video...");
            Debug.WriteLine("Monitoring Started");

            currentMonitorTask = MonitorAudioLevels(monitorTokenSource.Token);
        }

        
        private void FindSpotifySession()
        {
            Debug.WriteLine("Finding Spotify audio session...");
            try
            {
                AudioSessionControl? foundSpotifySession = null;
                List<AudioSessionControl> sessionsCopy = null;

                lock (playbackDeviceLock)
                {
                    if (playbackDevice == null || playbackDevice.State != DeviceState.Active)
                    {
                        Debug.WriteLine("Playback device is not active, cannot find Spotify session.");
                        return;
                    }

                    // Copy the sessions out of the lock
                    sessionsCopy = new List<AudioSessionControl>();
                    for (int i = 0; i < playbackDevice.AudioSessionManager.Sessions.Count; i++)
                    {
                        sessionsCopy.Add(playbackDevice.AudioSessionManager.Sessions[i]);
                    }
                }

                // Work on sessionsCopy outside the lock
                foreach (var session in sessionsCopy)
                {
                    Process proc;
                    try
                    {
                        proc = Process.GetProcessById((int)session.GetProcessID);
                    }
                    catch
                    {
                        continue;
                    }

                    if (proc.ProcessName.ToLower().Contains("spotify"))
                    {
                        foundSpotifySession = session;
                        Debug.WriteLine("[Session] Spotify session located.");
                        break;
                    }
                }

                if (foundSpotifySession != null && !isSpotifyActive)
                {
                    spotifySession = foundSpotifySession;
                    isSpotifyActive = true;

                    Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify detected! Ready to monitor.");
                    Debug.WriteLine("Spotify session found and updated.");
                }
                else if (foundSpotifySession == null && isSpotifyActive)
                {
                    spotifySession = null;
                    isSpotifyActive = false;

                    Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify session lost! Please re-start Spotify.");
                    Debug.WriteLine("Spotify session not found. Resetting.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding Spotify session: {ex.Message}");
            }
        }


        private async Task MonitorAudioLevels(CancellationToken cancellationToken)
        {
            Debug.WriteLine("🎧 Starting audio level monitoring loop...");

            bool spotifyMuted = false;
            float audioThreshold = 0.015f;
            TimeSpan unmuteDelay = TimeSpan.FromSeconds(2);
            DateTime lastVideoAudioDetected = DateTime.UtcNow;
            DateTime lastDeviceCheck = DateTime.UtcNow;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    //bool skipIteration = false;
                    List<AudioSessionControl> sessionsCopy = null;

                    lock (playbackDeviceLock)
                    {
                        if (playbackDevice == null || playbackDevice.State != DeviceState.Active ||
                            (DateTime.UtcNow - lastDeviceCheck).TotalSeconds > 10)
                        {
                            Debug.WriteLine("[Monitor] Refreshing playback device...");
                            playbackDevice?.Dispose();
                            enumerator?.Dispose();
                            enumerator = new MMDeviceEnumerator();
                            playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                            lastDeviceCheck = DateTime.UtcNow;
                        }

                        sessionsCopy = new List<AudioSessionControl>();
                        for (int i = 0; i < playbackDevice.AudioSessionManager.Sessions.Count; i++)
                            sessionsCopy.Add(playbackDevice.AudioSessionManager.Sessions[i]);
                    }
                            
                    bool videoDetected = false;

                    foreach (var session in sessionsCopy)
                    {
                        Process proc;

                        try { proc = Process.GetProcessById((int)session.GetProcessID); } catch { continue;  }
                        string name = proc.ProcessName.ToLower();

                        if (name.Contains("chrome") || name.Contains("edge") || name.Contains("vlc"))
                        {
                            //float videoAudioLevel = session.AudioMeterInformation.MasterPeakValue;
                            float level = session.AudioMeterInformation.MasterPeakValue;
                            if (level > audioThreshold)
                            {
                                videoDetected = true;
                                lastVideoAudioDetected = DateTime.UtcNow;

                                Debug.WriteLine($"[Monitor] Video playing in {name}: {level}");
                            }
                        }
                    }

                    if (spotifySession != null)
                    {
                        var simpleVolume = spotifySession.SimpleAudioVolume;
                        Debug.WriteLine($"[Monitor] Live Spotify volume: {simpleVolume.Volume:F2}");

                        if (videoDetected && currentMuteMode != SpotifyMuteMode.None)
                        {


                            //previousSpotifyVolume = simpleVolume.Volume;

                            //previousSpotifyVolume = Math.Clamp(previousSpotifyVolume, 0.0f, 1.0f);
                            if (!hasStoredOriginalVolume)
                            {
                                previousSpotifyVolume = simpleVolume.Volume;
                                hasStoredOriginalVolume = true; // Set the flag to true after storing the volume
                                Debug.WriteLine($"[Monitor] Original Spotify volume stored: {previousSpotifyVolume:F2}");
                            }

                            if (currentMuteMode == SpotifyMuteMode.Pause)
                            {
                                PauseSpotify();
                                //currentMuteMode = SpotifyMuteMode.Pause;
                                Debug.WriteLine("[Monitor] Paused Spotify (pause mode) ▶️");
                                Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify paused for video. Waiting to replay... ▶️");
                            }
                            else
                            {
                                simpleVolume.Volume = loweredSpotifyVolume;
                                //currentMuteMode = SpotifyMuteMode.Volume;
                                Debug.WriteLine($"[Monitor] Restored Spotify volume to {loweredSpotifyVolume}"); // changed from previous to lowered check this if audip levels are wrong
                                Dispatcher.Invoke(() => StatusTextBlock.Text = $"Spotify volume lowered to {loweredSpotifyVolume:P0} 🔉 "); // Update the UI with the restored volume
                            }

                            //currentMuteMode = currentMuteMode == SpotifyMuteMode.Pause ? SpotifyMuteMode.Pause : SpotifyMuteMode.Volume;
                            //Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify adjusted for video.");

                            //spotifyMuted = true;
                            //Debug.WriteLine($"[Monitor] Spotify audio dropped (previous volume: {previousSpotifyVolume})");
                            //Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify muted for video.");
                        }
                        else if (!videoDetected && currentMuteMode != SpotifyMuteMode.None)
                        {
                            TimeSpan sinceLast = DateTime.UtcNow - lastVideoAudioDetected;

                            if (sinceLast > unmuteDelay)
                            {
                                if (currentMuteMode == SpotifyMuteMode.Pause)
                                {
                                    ResumeSpotify();
                                    Debug.WriteLine("[Monitor] Resume Spotify (pause mode) ▶️");
                                    Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify resumed. 🔊");
                                }        
                                else if (currentMuteMode == SpotifyMuteMode.Volume && hasStoredOriginalVolume)
                                {
                                    simpleVolume.Volume = previousSpotifyVolume;
                                    Debug.WriteLine($"[Monitor] Restored Spotify volume to {previousSpotifyVolume}"); // changed from previous to lowered check this if audip levels are wrong
                                    Dispatcher.Invoke(() => StatusTextBlock.Text = $"Spotify volume restored to {previousSpotifyVolume:P0}"); // Update the UI with the restored volume
                                }

                                //currentMuteMode = SpotifyMuteMode.None;
                                hasStoredOriginalVolume = false; // Reset the flag after restoring volume
                                //Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify volume/playback restored.");
                            }
                        }
                        else if (!videoDetected)
                        {
                            Debug.WriteLine("[Monitor] No video detected, no action taken. Spotify playing at normal volume.");
                            Dispatcher.Invoke(() => StatusTextBlock.Text = "No video detected. Spotify playing at normal volume.");
                        }
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("Monitor task canceled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in monitor loop: {ex.Message}");
            }

            Debug.WriteLine("⏹️ Audio monitor stopped.");
        }

        protected override void OnClosed(EventArgs e)
        {
            Debug.WriteLine("[App] Closing application. cleaning up resources...");

            monitorTokenSource?.Cancel();

            // If Spotify is still muted (via volume or pause), restore it before closing
            if (spotifySession != null && currentMuteMode != SpotifyMuteMode.None)
            {
                var simpleVolume = spotifySession.SimpleAudioVolume;

                if (currentMuteMode == SpotifyMuteMode.Volume)
                {
                    simpleVolume.Volume = previousSpotifyVolume;
                    Debug.WriteLine($"[App] Restored Spotify volume to {previousSpotifyVolume}");
                }
                else if (currentMuteMode == SpotifyMuteMode.Pause)
                {
                    ResumeSpotify();
                    Debug.WriteLine("[App] Resumed Spotify playback before closing.");
                }

                lock (playbackDeviceLock)
                {
                    playbackDevice?.Dispose();
                    enumerator?.Dispose();
                }

                base.OnClosed(e);
            }
        }
    }
}
