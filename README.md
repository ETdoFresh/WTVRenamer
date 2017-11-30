# WTVRenamer
C# Program that renames WTV files on Media Center

## Getting Started
This project will create an executable that will rename files to (Series) - S##E##.wtv format for use with programs such as plex. 
It uses TVDB database to determine season and episode number. 
All paths can be found in WTVRenamer.cfg.

### Prerequisites
Visual Studio that can compile .NET 2.0+

### Installing
Copy files WTVRenamer.exe and Interop.Shell32.dll to directory of choice (preferably Recorded TV directory).
Schedule to run every 30 minutes on XX:07 and XX:37.

### Setup
After first run, you can change the paths in WTVRenamer.cfg.
Program logs actions to WTVRenamer.log.
