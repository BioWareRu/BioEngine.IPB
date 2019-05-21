﻿using System;
using System.Threading.Tasks;
using BioEngine.Core.DB;
using BioEngine.Core.Entities;
using BioEngine.Core.Publishers;
using BioEngine.Core.Web;
using BioEngine.Extra.IPB.Api;
using BioEngine.Extra.IPB.Models;
using Microsoft.Extensions.Logging;

namespace BioEngine.Extra.IPB.Publishing
{
    public class IPBContentPublisher : BaseContentPublisher<IPBPublishConfig, IPBPublishRecord>
    {
        private readonly IPBApiClientFactory _apiClientFactory;
        private readonly IContentRender _contentRender;

        public IPBContentPublisher(IPBApiClientFactory apiClientFactory, IContentRender contentRender,
            BioContext dbContext,
            ILogger<IContentPublisher<IPBPublishConfig>> logger) : base(dbContext, logger)
        {
            _apiClientFactory = apiClientFactory;
            _contentRender = contentRender;
        }

        protected override async Task<IPBPublishRecord> DoPublishAsync(IContentEntity entity, Site site,
            IPBPublishConfig config)
        {
            return await CreateOrUpdateContentPostAsync(entity, site, config);
        }

        protected override async Task<bool> DoDeleteAsync(IPBPublishRecord record, IPBPublishConfig config)
        {
            var apiClient = _apiClientFactory.GetClient(config.AccessToken);
            var result = await apiClient.PostAsync<TopicCreateModel, Topic>(
                $"forums/topics/{record.TopicId.ToString()}",
                new TopicCreateModel {Hidden = 1});
            return result.Hidden;
        }

        private async Task<IPBPublishRecord> CreateOrUpdateContentPostAsync(IContentEntity item, Site site,
            IPBPublishConfig config)
        {
            if (_contentRender == null)
            {
                throw new ArgumentException("No content renderer is registered!");
            }

            var apiClient = _apiClientFactory.GetClient(config.AccessToken);
            var record = await GetRecordAsync(item) ?? new IPBPublishRecord
            {
                ContentId = item.Id, Type = item.GetType().FullName
            };

            if (record.TopicId == 0)
            {
                var topic = new TopicCreateModel
                {
                    Forum = config.ForumId,
                    Title = item.Title,
                    Hidden = !item.IsPublished ? 1 : 0,
                    Post = await _contentRender.RenderHtmlAsync(item, site)
                };
                var createdTopic = await apiClient.PostAsync<TopicCreateModel, Topic>("forums/topics", topic);
                if (createdTopic.FirstPost != null)
                {
                    record.TopicId = createdTopic.Id;
                    record.PostId = createdTopic.FirstPost.Id;
                }
            }
            else
            {
                var topic = await apiClient.PostAsync<TopicCreateModel, Topic>(
                    $"forums/topics/{record.TopicId.ToString()}",
                    new TopicCreateModel {Title = item.Title, Hidden = !item.IsPublished ? 1 : 0});
                if (topic.FirstPost != null)
                {
                    await apiClient.PostAsync<PostCreateModel, Models.Post>(
                        $"forums/posts/{topic.FirstPost.Id.ToString()}",
                        new PostCreateModel {Post = await _contentRender.RenderHtmlAsync(item, site)});
                }
            }

            return record;
        }
    }
}