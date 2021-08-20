﻿// Copyright (c) IEvangelist. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.CosmosRepository.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.CosmosRepository.Providers
{
    /// <inheritdoc/>
    class DefaultCosmosContainerProvider<TItem>
        : ICosmosContainerProvider<TItem> where TItem : IItem
    {
        readonly Lazy<Task<Container>> _lazyContainer;
        readonly RepositoryOptions _options;
        readonly ICosmosClientProvider _cosmosClientProvider;
        private readonly ICosmosItemConfigurationProvider _cosmosItemConfigurationProvider;
        readonly ILogger<DefaultCosmosContainerProvider<TItem>> _logger;

        public DefaultCosmosContainerProvider(
            ICosmosClientProvider cosmosClientProvider,
            IOptions<RepositoryOptions> options,
            ICosmosItemConfigurationProvider cosmosItemConfigurationProvider,
            ILogger<DefaultCosmosContainerProvider<TItem>> logger)
        {
            _cosmosClientProvider = cosmosClientProvider
                ?? throw new ArgumentNullException(
                    nameof(cosmosClientProvider), "Cosmos client provider is required.");
            _cosmosItemConfigurationProvider = cosmosItemConfigurationProvider
                ?? throw new ArgumentNullException(nameof(cosmosItemConfigurationProvider) , "Cosmos item configuration provider is required.");

            _options = options?.Value
                       ?? throw new ArgumentNullException(
                           nameof(options), "Repository options are required.");

            _logger = logger
                ?? throw new ArgumentNullException($"The {nameof(logger)} is required.");

            if (_options.CosmosConnectionString is null)
            {
                throw new ArgumentNullException($"The {nameof(_options.CosmosConnectionString)} is required.");
            }
            if (_options.ContainerPerItemType is false)
            {
                if (_options.DatabaseId is null)
                {
                    throw new ArgumentNullException(
                        $"The {nameof(_options.DatabaseId)} is required when container per item type is false.");
                }
                if (_options.ContainerId is null)
                {
                    throw new ArgumentNullException(
                        $"The {nameof(_options.ContainerId)} is required when container per item type is false.");
                }
            }

            _lazyContainer = new Lazy<Task<Container>>(async () => await GetContainerValueFactoryAsync());
        }

        /// <inheritdoc/>
        public Task<Container> GetContainerAsync() => _lazyContainer.Value;

        async Task<Container> GetContainerValueFactoryAsync()
        {
            try
            {
                ItemOptions itemOptions = _cosmosItemConfigurationProvider.GetOptions<TItem>();

                Database database =
                    await _cosmosClientProvider.UseClientAsync(
                        client => client.CreateDatabaseIfNotExistsAsync(_options.DatabaseId)).ConfigureAwait(false);

                ContainerProperties containerProperties = new()
                {
                    Id = _options.ContainerPerItemType
                        ? itemOptions.ContainerName
                        : _options.ContainerId,
                    PartitionKeyPath = itemOptions.PartitionKeyPath
                };

                // Setting containerProperties.UniqueKeyPolicy to null throws, prevent that issue.
                UniqueKeyPolicy uniqueKeyPolicy = itemOptions.UniqueKeyPolicy;
                if (uniqueKeyPolicy is not null)
                {
                    containerProperties.UniqueKeyPolicy = uniqueKeyPolicy;
                }

                Container container =
                    await database.CreateContainerIfNotExistsAsync(
                        containerProperties).ConfigureAwait(false);

                return container;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);

                throw;
            }
        }
    }
}
