using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TrendingMoviesBanner.Api
{
    [ApiController]
    [Route("TrendingMoviesBanner")]
    public class TrendingController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;

        public TrendingController(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        [HttpGet("movies")]
        [Authorize]
        public async Task<ActionResult<List<BaseItemDto>>> GetTrendingMovies()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return NotFound();
            }

            var movies = await plugin.GetTrendingMoviesInLibrary(_libraryManager, plugin.Configuration.TopMoviesCount);

            var dtos = new List<BaseItemDto>();
            foreach (var movie in movies)
            {
                dtos.Add(new BaseItemDto
                {
                    Id = movie.Id,
                    Name = movie.Name,
                    ServerId = movie.Id.ToString(),
                    Type = BaseItemKind.Movie
                });
            }

            return Ok(dtos);
        }

        [HttpGet("script")]
        [AllowAnonymous]
        public ContentResult GetClientScript()
        {
            var script = @"(function() {
    const CACHE_KEY = 'trendingMoviesBanner';
    const CACHE_DURATION = 3600000;

    async function fetchTrendingMovies() {
        try {
            const serverUrl = window.location.origin;
            const token = ApiClient.accessToken();
            const response = await window.fetch(serverUrl + '/TrendingMoviesBanner/movies', {
                headers: {
                    'X-Emby-Token': token
                }
            });

            if (!response.ok) return [];
            return await response.json();
        } catch (error) {
            console.error('Error fetching trending movies:', error);
            return [];
        }
    }

    function createBanner(movies) {
        if (!movies || movies.length === 0) return;

        const homePage = document.querySelector('.homePage');
        if (!homePage) return;

        const existingBanner = document.getElementById('trending-movies-banner');
        if (existingBanner) existingBanner.remove();

        const banner = document.createElement('div');
        banner.id = 'trending-movies-banner';
        banner.style.cssText = 'width: 100%; height: 400px; position: relative; overflow: hidden; margin: 20px 0; border-radius: 8px;';

        const slider = document.createElement('div');
        slider.style.cssText = 'display: flex; transition: transform 0.5s ease; height: 100%;';

        movies.slice(0, 10).forEach((movie, index) => {
            const slide = document.createElement('div');
            const backdropUrl = ApiClient.getImageUrl(movie.Id, { type: 'Backdrop', maxWidth: 1920 });
            slide.style.cssText = 'min-width: 100%; height: 100%; position: relative; background: linear-gradient(to right, rgba(0,0,0,0.8), transparent), url(' + backdropUrl + '); background-size: cover; background-position: center; display: flex; align-items: center; padding: 40px; cursor: pointer;';

            const content = document.createElement('div');
            content.style.cssText = 'max-width: 600px; color: white;';

            const title = document.createElement('h2');
            title.style.cssText = 'font-size: 2.5em; margin: 0 0 10px 0;';
            title.textContent = movie.Name;

            const buttonContainer = document.createElement('div');
            buttonContainer.style.cssText = 'margin-top: 20px;';

            const playButton = document.createElement('button');
            playButton.style.cssText = 'padding: 12px 30px; font-size: 1.1em; background: #00a4dc; border: none; border-radius: 4px; color: white; cursor: pointer;';
            playButton.textContent = 'Play Now';

            buttonContainer.appendChild(playButton);
            content.appendChild(title);
            content.appendChild(buttonContainer);
            slide.appendChild(content);

            slide.addEventListener('click', function() {
                window.location.href = '#!/details?id=' + movie.Id;
            });

            slider.appendChild(slide);
        });

        banner.appendChild(slider);

        if (movies.length > 1) {
            let currentIndex = 0;

            const prevBtn = createNavButton('<', function() {
                currentIndex = (currentIndex - 1 + movies.length) % movies.length;
                slider.style.transform = 'translateX(-' + (currentIndex * 100) + '%)';
            });

            const nextBtn = createNavButton('>', function() {
                currentIndex = (currentIndex + 1) % movies.length;
                slider.style.transform = 'translateX(-' + (currentIndex * 100) + '%)';
            });

            banner.appendChild(prevBtn);
            banner.appendChild(nextBtn);

            setInterval(function() {
                currentIndex = (currentIndex + 1) % movies.length;
                slider.style.transform = 'translateX(-' + (currentIndex * 100) + '%)';
            }, 5000);
        }

        homePage.insertBefore(banner, homePage.firstChild);
    }

    function createNavButton(text, onClick) {
        const btn = document.createElement('button');
        btn.textContent = text;
        btn.style.cssText = 'position: absolute; top: 50%; transform: translateY(-50%); background: rgba(0,0,0,0.5); color: white; border: none; font-size: 2em; padding: 10px 20px; cursor: pointer; z-index: 10;';
        btn.style[text === '<' ? 'left' : 'right'] = '20px';
        btn.addEventListener('click', onClick);
        return btn;
    }

    async function loadBanner() {
        const cached = localStorage.getItem(CACHE_KEY);
        const cacheTime = localStorage.getItem(CACHE_KEY + '_time');

        if (cached && cacheTime && Date.now() - parseInt(cacheTime) < CACHE_DURATION) {
            createBanner(JSON.parse(cached));
        } else {
            const movies = await fetchTrendingMovies();
            if (movies.length > 0) {
                localStorage.setItem(CACHE_KEY, JSON.stringify(movies));
                localStorage.setItem(CACHE_KEY + '_time', Date.now().toString());
                createBanner(movies);
            }
        }
    }

    // Check if we're already on home page when script loads
    function checkInitialPage() {
        const homePage = document.querySelector('.homePage');
        if (homePage) {
            loadBanner();
        }
    }

    // Listen for navigation to home page
    document.addEventListener('viewshow', async function(e) {
        if (e.detail.type === 'home') {
            loadBanner();
        }
    });

    // Check immediately when script loads
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            setTimeout(checkInitialPage, 500);
        });
    } else {
        setTimeout(checkInitialPage, 500);
    }
})();";

            return Content(script, "application/javascript");
        }
    }
}
