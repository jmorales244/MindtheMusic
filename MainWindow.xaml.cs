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

        private CancellationTokenSource monitorTokenSource = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private void StartMonitoring()
        {
            StatusTextBlock.Text = "Status: Monitoring...";

            enumerator = new MMDeviceEnumerator();
            playbackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            FindSpotifySession();

            // Re-scan Spotify session every 15 mins
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(15));
                    FindSpotifySession();
                }
            });

            // Start the audio monitoring loop
            monitorTokenSource = new CancellationTokenSource();
            MonitorAudioLevels(monitorTokenSource.Token);
        }

        private void FindSpotifySession()
        {
            try
            {
                var sessions = playbackDevice.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
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
                        spotifySession = session;
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "Spotify session found!";
                        });
                        Debug.WriteLine("Spotify session found!");
                        return;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = "Spotify not running!";
                });
                Debug.WriteLine("Spotify not found.");
                spotifySession = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding Spotify session: {ex.Message}");
            }
        }

        private void MonitorAudioLevels(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                Debug.WriteLine("🎧 Starting audio level monitoring loop...");

                bool spotifyMuted = false;
                float audioThreshold = 0.015f; // Adjust threshold as needed
                TimeSpan unmuteDelay = TimeSpan.FromSeconds(3); // Delay before unmuting Spotify

                DateTime lastVideoAudioDetected = DateTime.UtcNow; // Tracks the last time we heard audio from video

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        MMDevice localPlaybackDevice = null;

                        try
                        {
                            localPlaybackDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get playback device: {ex.Message}");
                            await Task.Delay(1000);
                            continue;
                        }

                        var sessions = localPlaybackDevice.AudioSessionManager.Sessions;

                        bool videoAudioDetected = false;
                        //AudioSessionControl? spotifySession = null;

                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
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

                            // Log the audio level for debugging purposes
                            if (name.Contains("chrome") || name.Contains("edge") || name.Contains("vlc"))
                            {
                                // Debugging, uncomment for previous audio level check
                                //float peak = session.AudioMeterInformation.MasterPeakValue;

                                //if (peak > 0.01f)
                                //{
                                    //videoAudioDetected = true;
                                    //Debug.WriteLine($"Video playing in {name}: {peak}");
                                //}

                                float videoAudioLevel = session.AudioMeterInformation.MasterPeakValue;

                                if (videoAudioLevel > audioThreshold) // Adjust threshold as needed
                                {
                                    videoAudioDetected = true;
                                    lastVideoAudioDetected = DateTime.UtcNow; // Update the last detected time
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
                                    previousSpotifyVolume = simpleVolume.Volume; // Save the current vol
                                    previousSpotifyVolume = Math.Clamp(previousSpotifyVolume, 0.0f, 1.0f); // Ensure it's within valid range
                                    
                                    simpleVolume.Volume = 0.0f; // Mute Spotify
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
                                    simpleVolume.Volume = previousSpotifyVolume; // Restore volume
                                    spotifyMuted = false;

                                    Debug.WriteLine($"Restored Spotify volume to {previousSpotifyVolume}");
                                    Dispatcher.Invoke(() => StatusTextBlock.Text = "Spotify volume restored.");
                                }
                            }
                        }

                        await Task.Delay(1000);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in monitor loop: {ex.Message}");
                        await Task.Delay(1000);
                    }
                }

                Debug.WriteLine("⏹️ Audio monitor stopped.");
            });
        }


        protected override void OnClosed(EventArgs e)
        {
            monitorTokenSource?.Cancel();
            enumerator?.Dispose();
            base.OnClosed(e);
        }
    }
}
