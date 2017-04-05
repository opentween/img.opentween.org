﻿using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom.Html;
using AngleSharp.Extensions;
using ImgAzyobuziNet.Core.SupportServices;
using ImgAzyobuziNet.TestFramework;
using Microsoft.Extensions.Caching.Memory;

namespace ImgAzyobuziNet.Core.Resolvers
{
    public class ImepicProvider : PatternProviderBase<ImepicResolver>
    {
        public override string ServiceId => "imepic";

        public override string ServiceName => "イメピク";

        public override string Pattern => @"^http://(?:www\.)?imepic.jp/(\d{8}/\d+)(?:[\?#]|$)";

        #region Tests

        [TestMethod(TestCategory.Static)]
        private void RegexTest()
        {
            var match = this.GetRegex().Match("http://imepic.jp/20151221/799860");
            Assert.True(() => match.Success);
            match.Groups[1].Value.Is("20151221/799860");
        }

        #endregion
    }

    public class ImepicResolver : IResolver
    {
        private readonly IHttpClient _httpClient;
        private readonly IResolverCache _memoryCache;

        public ImepicResolver(IHttpClient httpClient, IResolverCache memoryCache)
        {
            this._httpClient = httpClient;
            this._memoryCache = memoryCache;
        }

        public async ValueTask<ImageInfo[]> GetImages(Match match)
        {
            var id = match.Groups[1].Value;
            var result = await this._memoryCache.GetOrSet(
                "imepic-" + id,
                () => this.Fetch(id)
            ).ConfigureAwait(false);

            return new[] { new ImageInfo(result.TwitterImage, result.TwitterImage, result.OgImage) };
        }

        private class CacheItem
        {
            public string OgImage;
            public string TwitterImage;
        }

        private async Task<CacheItem> Fetch(string id)
        {
            IHtmlDocument document;
            var req = new HttpRequestMessage(HttpMethod.Get, "http://imepic.jp/" + id);

            using (var res = await this._httpClient.SendAsync(req).ConfigureAwait(false))
            {
                if (res.StatusCode == HttpStatusCode.NotFound)
                    throw new ImageNotFoundException();

                res.EnsureSuccessStatusCode();
                document = await res.Content.ReadAsHtmlDocument().ConfigureAwait(false);
            }

            string ogImage = null;
            string twitterImage = null;

            foreach (var node in document.Head.ChildNodes)
            {
                var element = node as IHtmlMetaElement;
                if (element == null) continue;

                if (ogImage == null && element.GetAttribute("property") == "og:image")
                {
                    ogImage = element.GetAttribute("content");
                    if (twitterImage != null) break;
                }
                else if (twitterImage == null && element.GetAttribute("name") == "twitter:image")
                {
                    twitterImage = element.GetAttribute("content");
                    if (ogImage != null) break;
                }
            }

            if (ogImage == null || twitterImage == null)
                throw new Exception("イメピク仕様変更の可能性");

            return new CacheItem
            {
                OgImage = ogImage,
                TwitterImage = twitterImage
            };
        }

        #region Tests

        [TestMethod(TestCategory.Network)]
        private async Task FetchTest()
        {
            // http://imepic.jp/20170322/630700
            // 保存期間 30 日なので気づいたらテスト通らなくなってるやつ
            var result = await this.Fetch("20170322/630700").ConfigureAwait(false);
            Assert.True(() => !string.IsNullOrEmpty(result.OgImage));
            Assert.True(() => !string.IsNullOrEmpty(result.TwitterImage));
        }

        #endregion
    }
}
