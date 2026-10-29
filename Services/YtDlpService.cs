
using System.Diagnostics;

namespace YTM2Local.Services
{
    public class YtDlpService
    {
        private readonly string _musicDirectory;
        private readonly string _archivePath;
        private readonly Action<string> _log;

        public YtDlpService(
            string musicDirectory = "/music",
            string archivePath = "/config/downloaded.txt",
            Action<string>? log = null)
        {
            _musicDirectory = musicDirectory;
            _archivePath = archivePath;
            _log = log ?? Console.WriteLine;
        }

        public async Task<int> DownloadPlaylistAsync(
            string playlistUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(playlistUrl))
                throw new ArgumentException(
                    "Playlist URL cannot be empty.",
                    nameof(playlistUrl));

            var startInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--extract-audio");
            startInfo.ArgumentList.Add("--audio-format");
            startInfo.ArgumentList.Add("mp3");

            startInfo.ArgumentList.Add("--audio-quality");
            startInfo.ArgumentList.Add("3");

            startInfo.ArgumentList.Add("--download-archive");
            startInfo.ArgumentList.Add(_archivePath);

            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(
                $"{_musicDirectory}/%(title)s [%(id)s].%(ext)s");

            startInfo.ArgumentList.Add(playlistUrl);

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
            Task stdoutTask = ReadStreamAsync(
                process.StandardOutput, "yt-dlp");

            Task stderrTask = ReadStreamAsync(
                process.StandardError, "yt-dlp");

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
                    // The process may already have exited.
                }

                await process.WaitForExitAsync();
                await Task.WhenAll(stdoutTask, stderrTask);

                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            int exitCode = process.ExitCode;

            if (exitCode == 0)
                _log("Playlist sync completed successfully.");
            else
                _log($"ERROR: yt-dlp exited with code {exitCode}.");

            return exitCode;
        }

        private async Task ReadStreamAsync(
            StreamReader reader,
            string source)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _log($"[{source}] {line}");
            }
        }
    }
}
