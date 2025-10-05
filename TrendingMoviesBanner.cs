using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.TrendingMoviesBanner
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<Plugin> _logger;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public Plugin(
            IApplicationPaths applicationPaths, 
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            ILogger<Plugin> logger
        ) : base(applicationPaths, xmlSerializer)
        {
            _libraryManager = libraryManager;
            _applicationPaths = applicationPaths;
            _logger = logger;
            Instance = this;
            
            // Automatically inject client script on startup
            InjectClientScript();
        }

        public override string Name => "Trending Movies Banner";
        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    EmbeddedResourcePath = string.Format("{0}.Configuration.config.html", GetType().Namespace)
                }
            };
        }

        private void InjectClientScript()
        {
            try
            {
                // Find the jellyfin-web directory
                string webPath = _applicationPaths.WebPath;
                if (string.IsNullOrEmpty(webPath))
                {
                    _logger.LogWarning("Web path is not set. Trying common locations...");
                    
                    // Try common paths
                    var possiblePaths = new[]
                    {
                        "/jellyfin/jellyfin-web",
                        "/usr/share/jellyfin/web",
                        "/usr/lib/jellyfin/bin/jellyfin-web",
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jellyfin-web")
                    };
                    
                    foreach (var path in possiblePaths)
                    {
                        if (Directory.Exists(path))
                        {
                            webPath = path;
                            _logger.LogInformation($"Found web path at: {webPath}");
                            break;
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath))
                {
                    _logger.LogWarning("Could not find jellyfin-web directory. Client script injection skipped. Users must manually add the script tag.");
                    return;
                }
                
                string indexPath = Path.Combine(webPath, "index.html");
                if (!File.Exists(indexPath))
                {
                    _logger.LogWarning($"index.html not found at {indexPath}");
                    return;
                }
                
                // Read the index.html file
                string indexContent = File.ReadAllText(indexPath);
                
                // Check if script is already injected
                string scriptTag = "<script plugin=\"TrendingMoviesBanner\" defer=\"defer\" version=\"1.0.0.0\" src=\"/TrendingMoviesBanner/script\"></script>";
                
                if (indexContent.Contains("TrendingMoviesBanner"))
                {
                    _logger.LogInformation("Client script already injected in index.html");
                    return;
                }
                
                // Inject before </body>
                if (indexContent.Contains("</body>"))
                {
                    indexContent = indexContent.Replace("</body>", $"{scriptTag}\n</body>");
                    File.WriteAllText(indexPath, indexContent);
                    _logger.LogInformation("Successfully injected client script into index.html");
                }
                else
                {
                    _logger.LogWarning("Could not find </body> tag in index.html");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Permission denied when trying to inject client script. Please ensure Jellyfin has write access to jellyfin-web/index.html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error injecting client script");
            }
        }

        public async Task<List<BaseItem>> GetTrendingMoviesInLibrary(int topCount = 10)
        {
            try
            {
                string apiKey = Configuration.TmdbApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("TMDb API key not configured");
                    return new List<BaseItem>();
                }

                string url = $"https://api.themoviedb.org/3/trending/movie/week?api_key={apiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var tmdbResult = JsonConvert.DeserializeObject<TmdbTrendingResponse>(response);

                if (tmdbResult?.Results == null)
                {
                    return new List<BaseItem>();
                }

                var jellyfinMovies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    IsVirtualItem = false,
                    Recursive = true
                }).ToList();

                var matchedMovies = new List<BaseItem>();
                
                foreach (var trendingMovie in tmdbResult.Results.Take(topCount * 3))
                {
                    var match = jellyfinMovies.FirstOrDefault(m =>
                        m.Name.Equals(trendingMovie.Title, StringComparison.OrdinalIgnoreCase) ||
                        (m.ProductionYear.HasValue && 
                         Math.Abs(m.ProductionYear.Value - GetYear(trendingMovie.ReleaseDate)) <= 1 &&
                         LevenshteinDistance(m.Name.ToLower(), trendingMovie.Title.ToLower()) <= 3)
                    );

                    if (match != null && !matchedMovies.Contains(match))
                    {
                        matchedMovies.Add(match);
                        if (matchedMovies.Count >= topCount)
                            break;
                    }
                }

                _logger.LogInformation($"Found {matchedMovies.Count} trending movies in library");
                return matchedMovies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching trending movies");
                return new List<BaseItem>();
            }
        }

        private int GetYear(string dateString)
        {
            if (DateTime.TryParse(dateString, out var date))
                return date.Year;
            return 0;
        }

        private int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
    }

    public class TmdbTrendingResponse
    {
        [JsonProperty("results")]
        public List<TmdbMovie> Results { get; set; }
    }

    public class TmdbMovie
    {
        [JsonProperty("title")]
        public string Title { get; set; }
        
        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }
        
        [JsonProperty("poster_path")]
        public string PosterPath { get; set; }
        
        [JsonProperty("backdrop_path")]
        public string BackdropPath { get; set; }
    }
}
