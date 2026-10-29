using Microsoft.Extensions.Configuration;

namespace YTM2Local
{
    internal class Program
    {
        static string[] playlistUrls = new string[0];
        static string musicDirectory = string.Empty;
        static string archivePath = string.Empty;

        static void Main(string[] args)
        {
            GetConfiguration();

            try
            {
                
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
    }
}
