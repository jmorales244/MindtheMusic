Resume Building App for Windows ! Mind the Music

**TL;DR:** I got tired of manually pausing Spotify every time I watched a lecture or wrote notes, so I built an app that does it automatically. It detects video playback and smartly controls Spotify without any internet or APIs.

Hey guys, do you listen to music while taking notes and listening to your lecture? Sometimes you really need to either pay attention and listen to your professor and write down something, so you switch window tabs, pause Spotify, then play your video, pause your video to write your notes/homework/etc. and then you continue forgetting you were jamming to whatever music you listen to!   


I ran into this little problem and decided to work on a solution. I love music, and working on homework while listening to classic music, brown noise, rap, rock, whatever; this app can fix this nuisance! I have been working on this app for months to strengthen my skills and well I think I am ready to release a beta version in order to get feedback and to see if it works with others! This is perfect as we near the finals, so you can test it out and let me know what you think!

# What it does:

* Monitors for video playback
* Automatically pauses or lowers Spotify when a video starts
* Restores Spotify when the video stops

It's built in C# using .NET 8 and WPF, I focused on privacy-first control, which means no Spotify API calls or internet services, everything is ran through commands within Windows. I’m still refining features and UI behavior. I’d appreciate:

* UX suggestions
* Feedback on how intuitive the controls feel
* Performance or bug reports

# Disclaimers:

* This is an early prototype!
* You need the Spotify app downloaded (So far I have been using Windows App, not sure if you downloaded Spotify from their website should work any differently so let me know if it does!)

# Future ideas:

* Auto-detect more media apps
* Background service mode (hidden in toolbar)
* Custom profile modes (gaming, study, etc.)


work in action!:
Lowers your volume:
![Low Audio](https://github.com/jmorales244/MindtheMusic/blob/master/assets/IMG_5977.gif)

Pauses your music:
![Pause Music](https://github.com/jmorales244/MindtheMusic/blob/master/assets/IMG_5978.gif)
