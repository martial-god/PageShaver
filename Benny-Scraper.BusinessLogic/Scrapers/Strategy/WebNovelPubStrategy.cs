﻿using Benny_Scraper.BusinessLogic.Config;
using Benny_Scraper.Models;
using HtmlAgilityPack;
using System.Collections.Specialized;
using System.Web;
using Benny_Scraper.BusinessLogic.Scrapers.Strategy.Impl;

namespace Benny_Scraper.BusinessLogic.Scrapers.Strategy
{
    namespace Impl
    {
        public class WebNovelPubInitializer : NovelDataInitializer
        {
            public static void FetchNovelContent(NovelData novelData, HtmlDocument htmlDocument, Uri tableOfContents, SiteConfiguration siteConfig)
            {
                var attributesToFetch = new List<Attr>()
                {
                    Attr.Title,
                    Attr.Author
                };
                foreach(var attribute in attributesToFetch)
                {
                    FetchContentByAttribute(attribute, novelData, htmlDocument, siteConfig);
                }
            }
        }
    }
    public class WebNovelPubStrategy : ScraperStrategy
    {
        private Uri? _chaptersUri;

        public override async Task<NovelData> ScrapeAsync()
        {
            Logger.Info("Starting scraper for Web");

            SetBaseUri(SiteTableOfContents);
            
            var htmlDocument = await LoadHtmlAsync(SiteTableOfContents);
            var novelData = FetchNovelDataFromTableOfContents(htmlDocument);

            _chaptersUri = new Uri(SiteTableOfContents + "/chapters");

            htmlDocument = await LoadHtmlAsync(_chaptersUri);

            var decodedHtmlDocument = DecodeHtml(htmlDocument);

            int pageToStopAt = GetLastTableOfContentsPageNumber(decodedHtmlDocument);

            var (chapterUrls, lastTableOfContentsUrl) = await GetPaginatedChapterUrlsAsync(_chaptersUri, true, pageToStopAt);
            novelData.ChapterUrls = chapterUrls;
            novelData.LastTableOfContentsPageUrl = lastTableOfContentsUrl;
            novelData.Genres = new List<string>();

            return novelData;
        }

        public override NovelData FetchNovelDataFromTableOfContents(HtmlDocument htmlDocument)
        {
            var novelData = new NovelData();
            try
            {
                WebNovelPubInitializer.FetchNovelContent(novelData, htmlDocument, SiteTableOfContents, SiteConfig);
                return novelData;
            }
            catch (Exception e)
            {
                Logger.Error($"Error occurred while getting novel data from table of contents. Error: {e}");
            }

            return novelData;
        }

        List<string> GetGenres(HtmlDocument htmlDocument, SiteConfiguration siteConfig)
        {
            throw new NotImplementedException();
        }

        private int GetLastTableOfContentsPageNumber(HtmlDocument htmlDocument)
        {
            HtmlNodeCollection paginationNodes = htmlDocument.DocumentNode.SelectNodes(SiteConfig.Selectors.TableOfContnetsPaginationListItems);
            int paginationCount = paginationNodes.Count;

            int pageToStopAt = 1;
            if (paginationCount > 1)
            {
                HtmlNode lastPageNode;
                if (paginationCount == TotalPossiblePaginationTabs)
                {
                    lastPageNode = htmlDocument.DocumentNode.SelectSingleNode(SiteConfig.Selectors.LastTableOfContentsPage);
                }
                else
                {
                    lastPageNode = paginationNodes[paginationCount - 2]; // Get the second last node which is the last page number
                    lastPageNode = lastPageNode.SelectSingleNode("a");
                }

                var lastPageUrl = lastPageNode.Attributes["href"].Value;
                var lastPageUri = new Uri(lastPageUrl, UriKind.RelativeOrAbsolute);

                // If the URL is relative, make sure to add a scheme and host
                if (!lastPageUri.IsAbsoluteUri) // like this: /novel/the-authors-pov-14051336/chapters?page=9
                {
                    lastPageUri = new Uri(BaseUri + lastPageUrl);
                }

                NameValueCollection query = HttpUtility.ParseQueryString(lastPageUri.Query);

                var pageNumber = query["page"];
                int.TryParse(pageNumber, out pageToStopAt);
            }

            return pageToStopAt;
        }
    }
}
