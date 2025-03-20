using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NAudio.CoreAudioApi; 
using Windows.Media.Control; // For AudioSessionControl
using Windows.Foundation; // For IAsyncAction

namespace MindtheMusic
{
    public partial class MainWindow : Window
    {
        private MMDeviceEnumerator? enumerator = null;
        private MMDevice? playbackDevice = null;
        private AudioSessionControl? spotifySession;
        private float previousSpotifyVolume = 1.0f;
        private bool isSpotifyActive = false; // Track if Spotify is active 
        private readonly object playbackDeviceLock = new object(); // Lock for playback device access
        private Task currentMonitorTask = Task.CompletedTask; // Track the current monitor task
        private CancellationTokenSource monitorTokenSource = null;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize devices immediately to check at start up
            enumerator = new MMDeviceEnumerator();
            playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            // Check Spotify running status right when app loads
            CheckSpotifyRunning();
        }

        private void CheckSpotifyRunning()
        {
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

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private async void StartMonitoring()
        {
            // Cancel previous token source and wait for task completion
            if (monitorTokenSource != null)
            {
                monitorTokenSource.Cancel();

                try
                {
                    await currentMonitorTask;  // Wait for the previous task to finish cleanly
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Previous monitor task ended with error: {ex.Message}");
                }

                monitorTokenSource.Dispose();
            }

            monitorTokenSource = new CancellationTokenSource();

            lock (playbackDeviceLock)
            {
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

            StatusTextBlock.Text = "Status: Monitoring...";
            Debug.WriteLine("Monitoring Started");

            currentMonitorTask = MonitorAudioLevels(monitorTokenSource.Token);
        }


        private void FindSpotifySession()
        {
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
            TimeSpan unmuteDelay = TimeSpan.FromSeconds(3);
            DateTime lastVideoAudioDetected = DateTime.UtcNow;
            DateTime lastDeviceCheck = DateTime.UtcNow;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool skipIteration = false;
                    List<AudioSessionControl> sessionsCopy = null;

                    lock (playbackDeviceLock)
                    {
                        if (playbackDevice == null || playbackDevice.State != DeviceState.Active ||
                            (DateTime.UtcNow - lastDeviceCheck).TotalSeconds > 10)
                        {
                            Debug.WriteLine("Refreshing playback device...");

                            try
                            {
                                playbackDevice?.Dispose();
                                enumerator?.Dispose();

                                enumerator = new MMDeviceEnumerator();
                                playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                                Debug.WriteLine("Playback device refreshed.");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error refreshing playback device: {ex.Message}");
                                skipIteration = true;
                            }

                            spotifySession = null;
                            isSpotifyActive = false;
                            lastDeviceCheck = DateTime.UtcNow;
                        }

                        if (!skipIteration)
                        {
                            sessionsCopy = new List<AudioSessionControl>();

                            for (int i = 0; i < playbackDevice.AudioSessionManager.Sessions.Count; i++)
                            {
                                sessionsCopy.Add(playbackDevice.AudioSessionManager.Sessions[i]);
                            }
                        }
                    }

                    if (skipIteration || sessionsCopy == null || sessionsCopy.Count == 0)
                    {
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    bool videoAudioDetected = false;

                    foreach (var session in sessionsCopy)
                    {
                        Process proc = null;

                        try
                        {
                            proc = Process.GetProcessById((int)session.GetProcessID);
                        }
                        catch
                        {
                            continue;
                        }

                        string name = proc.ProcessName.ToLower();

                        if (name.Contains("spotify"))
                        {
                            spotifySession = session;
                        }

                        if (name.Contains("chrome") || name.Contains("edge") || name.Contains("vlc"))
                        {
                            float videoAudioLevel = session.AudioMeterInformation.MasterPeakValue;

                            if (videoAudioLevel > audioThreshold)
                            {
                                videoAudioDetected = true;
                                lastVideoAudioDetected = DateTime.UtcNow;

                                Debug.WriteLine($"Video playing in {name}: {videoAudioLevel}");
                            }
                        }
                    }

                    if (spotifySession != null)
                    {
                        var simpleVolume = spotifySession.SimpleAudioVolume;

                        if (videoAudioDetected)
                        {
                            if (!spotifyMuted)
                            {
                                previousSpotifyVolume = simpleVolume.Volume;
                                previousSpotifyVolume = Math.Clamp(previousSpotifyVolume, 0.0f, 1.0f);

                                simpleVolume.Volume = 0.0f;
                                spotifyMuted = true;

                                Debug.WriteLine($"Muted Spotify (previous volume: {previousSpotifyVolume})");
                                Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify muted for video.");
                            }
                        }
                        else
                        {
                            TimeSpan timeSinceLastVideoAudio = DateTime.UtcNow - lastVideoAudioDetected;

                            if (spotifyMuted && timeSinceLastVideoAudio > unmuteDelay)
                            {
                                simpleVolume.Volume = previousSpotifyVolume;
                                spotifyMuted = false;

                                Debug.WriteLine($"Restored Spotify volume to {previousSpotifyVolume}");
                                Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify volume restored.");
                            }
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
            monitorTokenSource?.Cancel();

            lock (playbackDeviceLock)
            {
                playbackDevice?.Dispose();
                enumerator?.Dispose();
            }

            base.OnClosed(e);
        }
    }
}
