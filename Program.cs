using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace YTM2Local
{
    internal class Program
    {
        static string[] playlistUrls = new string[0];
        static string musicDirectory = string.Empty;
        static string archivePath = string.Empty;
        private readonly Action<string> _log = Console.WriteLine;

        static async Task Main(string[] args)
        {
            try
            {
                GetConfiguration();

                Program program = new Program();

                await program.UpdateYtDlpAsync();

                Console.WriteLine($"===== Starting YouTube Sync: {DateTime.Now} =====");

                foreach (var playlistUrl in playlistUrls)
                {
                    if (string.IsNullOrWhiteSpace(playlistUrl) || playlistUrl.StartsWith('#'))
                        continue;

                    int result = await program.DownloadPlaylistAsync(playlistUrl);

                    if (result != 0)
                    {
                        Console.WriteLine($"WARNING: Playlist download failed: {playlistUrl}");
                    }
                }

                GenerateM3UPlaylists();

                Console.WriteLine("===== Sync Task Finished: " + DateTime.Now + " =====");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        internal static void GetConfiguration()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();

            playlistUrls = config.GetValue<string[]>("Appsettings:playlistUrls") ?? throw new InvalidOperationException("Playlist URLs not found in configuration.");
            musicDirectory = config.GetValue<string>("Appsettings:musicDirectory") ?? throw new InvalidOperationException("Music directory not found in configuration.");
            archivePath = config.GetValue<string>("Appsettings:archivePath") ?? throw new InvalidOperationException("Archive path not found in configuration.");
        }

        internal async Task<int> DownloadPlaylistAsync(string playlistUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                throw new ArgumentException("Playlist URL cannot be empty.", nameof(playlistUrl));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            List<string> argumentList = new List<string>
                {
                    "--js-runtimes",
                    "node",
                    "--extract-audio",
                    "--audio-format",
                    "mp3",
                    "--audio-quality",
                    "0",
                    "--embed-thumbnail",
                    "--embed-metadata",
                    "--download-archive",
                    archivePath,
                    "--output",
                    Path.Combine(musicDirectory, "%(playlist_title,playlist)s", "%(playlist_index)05d - %(title)s.%(ext)s"),
                    "--parse-metadata",
                    "playlist_title:%(album)s",
                    "--parse-metadata",
                    "Various Artists:%(album_artist)s",
                    "--ignore-errors",
                    playlistUrl
                };

            foreach (string argument in argumentList)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process
            {
                StartInfo = startInfo
            };

            _log($"Starting playlist sync: {playlistUrl}");

            try
            {
                if (!process.Start())
                {
                    _log("ERROR: Failed to start yt-dlp.");
                    return -1;
                }
            }
            catch (Exception ex) when (
                ex is System.ComponentModel.Win32Exception ||
                ex is InvalidOperationException)
            {
                _log($"ERROR: Could not start yt-dlp: {ex.Message}");
                return -1;
            }

            // Read both streams concurrently to prevent blocking.
            Task stdoutTask = ReadStreamAsync(process.StandardOutput, "yt-dlp");

            Task stderrTask = ReadStreamAsync(process.StandardError, "yt-dlp");

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _log("Cancellation requested. Stopping yt-dlp.");

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process maybe has already exited
                }

                await process.WaitForExitAsync();
                await Task.WhenAll(stdoutTask, stderrTask);

                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            int exitCode = process.ExitCode;

            if (exitCode == 0)
                _log($"Playlist sync completed successfully.");
            else
                _log($"ERROR: yt-dlp exited with code {exitCode}.");

            return exitCode;
        }

        internal async Task ReadStreamAsync(StreamReader reader,string source)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _log($"[{source}] {line}");
            }
        }

        internal static void GenerateM3UPlaylists()
        {
            foreach (string directory in Directory.GetDirectories(musicDirectory))
            {
                string playlistName = Path.GetFileName(directory);

                string playlistPath = Path.Combine(
                    directory,
                    $"{playlistName}.m3u"
                );

                string[] mp3Files = Directory.GetFiles(
                    directory,
                    "*.mp3",
                    SearchOption.AllDirectories
                );

                // Sort by filename (which contains the YouTube playlist index).
                mp3Files = mp3Files
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                // Generate relative paths for Navidrome.
                string[] playlistEntries = mp3Files
                    .Select(file => Path.GetRelativePath(directory, file)
                        .Replace('\\', '/'))
                    .ToArray();

                File.WriteAllLines(
                    playlistPath,
                    playlistEntries,
                    new System.Text.UTF8Encoding(false)
                );
            }
        }

        internal async Task UpdateYtDlpAsync()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-U");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            _log("Checking for yt-dlp updates...");

            try
            {
                if (!process.Start())
                {
                    _log("ERROR: Could not start yt-dlp update.");
                    return;
                }

                Task stdoutTask = ReadStreamAsync(process.StandardOutput, "yt-dlp OUT");
                Task stderrTask = ReadStreamAsync(process.StandardError, "yt-dlp ERR");

                await process.WaitForExitAsync();

                await Task.WhenAll(stdoutTask, stderrTask);

                if (process.ExitCode == 0)
                    _log("yt-dlp update check completed.");
                else
                    _log($"WARNING: yt-dlp update returned exit code {process.ExitCode}.");
            }
            catch (Exception ex) when (
                ex is System.ComponentModel.Win32Exception ||
                ex is InvalidOperationException)
            {
                _log($"WARNING: Could not update yt-dlp: {ex.Message}");
            }
        }
    }
}
