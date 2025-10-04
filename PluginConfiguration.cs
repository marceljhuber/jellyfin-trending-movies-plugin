using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TrendingMoviesBanner
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string TmdbApiKey { get; set; }
        public int TopMoviesCount { get; set; }
        public int RefreshIntervalHours { get; set; }

        public PluginConfiguration()
        {
            TmdbApiKey = "";
            TopMoviesCount = 10;
            RefreshIntervalHours = 24;
        }
    }
}
