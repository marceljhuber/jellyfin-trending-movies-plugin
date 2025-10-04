using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.TrendingMoviesBanner
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILibraryManager _libraryManager;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public Plugin(
            IApplicationPaths applicationPaths, 
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager
        ) : base(applicationPaths, xmlSerializer)
        {
            _libraryManager = libraryManager;
            Instance = this;
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
                    Name = "TrendingMoviesBanner",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
                }
            };
        }

        public async Task<List<BaseItem>> GetTrendingMoviesInLibrary(int topCount = 10)
        {
            try
            {
                // Get TMDb API key from configuration
                string apiKey = Configuration.TmdbApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    return new List<BaseItem>();
                }

                // Fetch trending movies from TMDb (weekly)
                string url = $"https://api.themoviedb.org/3/trending/movie/week?api_key={apiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var tmdbResult = JsonConvert.DeserializeObject<TmdbTrendingResponse>(response);

                if (tmdbResult?.Results == null)
                {
                    return new List<BaseItem>();
                }

                // Get all movies from Jellyfin library
                var jellyfinMovies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Movie) },
                    IsVirtualItem = false,
                    Recursive = true
                }).ToList();

                // Match trending movies with library
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

                return matchedMovies;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching trending movies: {ex.Message}");
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
