// Copyright (c) IEvangelist. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.CosmosEventSourcing;
using Microsoft.Azure.CosmosEventSourcing.Events;
using Microsoft.Azure.CosmosEventSourcing.Exceptions;
using Microsoft.Azure.CosmosEventSourcing.Stores;
using Microsoft.Azure.CosmosRepository;
using Microsoft.Azure.CosmosRepository.Paging;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace Microsoft.Azure.CosmosEventSourcingTests;

public class EventStoreTests
{
    private readonly AutoMocker _autoMocker = new();
    private readonly Mock<IRepository<Testing.SampleEventItem>> _repository;
    private const string Pk = "pk";

    private readonly List<Testing.SampleEventItem> _events = new()
    {
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
    };

    private readonly List<Testing.SampleEventItem> _eventsWithAtomicEvents = new()
    {
        new Testing.SampleEventItem(new AtomicEvent(Guid.NewGuid(), string.Empty), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
        new Testing.SampleEventItem(new Testing.SampleEvent(DateTime.UtcNow), Pk),
    };

    public EventStoreTests() =>
        _repository = _autoMocker.GetMock<IRepository<Testing.SampleEventItem>>();

    private IEventStore<Testing.SampleEventItem> CreateSut() =>
        _autoMocker.CreateInstance<DefaultEventStore<Testing.SampleEventItem>>();

    [Fact]
    public async Task PersistAsync_Events_SavesAllEvents()
    {
        //Arrange
        IEventStore<Testing.SampleEventItem> sut = CreateSut();

        //Act
        await sut.PersistAsync(_eventsWithAtomicEvents);

        //Assert
        _repository.Verify(o =>
            o.UpdateAsBatchAsync(
                _eventsWithAtomicEvents,
                default));
    }

    [Fact]
    public async Task PersistAsync_NoEvents_DoesNotSaveBatch()
    {
        //Arrange
        IEventStore<Testing.SampleEventItem> sut = CreateSut();

        //Act
        await sut.PersistAsync(new List<Testing.SampleEventItem>());

        //Assert
        _repository.Verify(o =>
            o.UpdateAsBatchAsync(
                It.IsAny<List<Testing.SampleEventItem>>(),
                default),
            Times.Never);
    }

    [Fact]
    public async Task PersistAsync_EventsWithAtomicEvent_ThrowAtomicEventRequiredExceptions()
    {
        //Arrange
        IEventStore<Testing.SampleEventItem> sut = CreateSut();

        //Act
        //Assert
        await Assert.ThrowsAsync<AtomicEventRequiredException>(() =>
            sut.PersistAsync(_events).AsTask());
    }

    [Fact]
    public async Task GetAsync_EventsInDb_GetsAllEvents()
    {
        //Arrange
        IEventStore<Testing.SampleEventItem> sut = CreateSut();

        _repository
            .Setup(o =>
                o.GetAsync(x => x.PartitionKey == Pk, default))
            .ReturnsAsync(_eventsWithAtomicEvents);

        //Act
        IEnumerable<Testing.SampleEventItem> got = await sut.ReadAsync(Pk);

        //Assert
        got.Should().BeEquivalentTo(_eventsWithAtomicEvents);
    }

    [Fact]
    public async Task StreamAsync_EventsInDb_StreamsAllEvents()
    {
        //Arrange
        IEventStore<Testing.SampleEventItem> sut = CreateSut();

        Page<Testing.SampleEventItem> page1 = new(
            null,
            5,
            _eventsWithAtomicEvents,
            0,
            Guid.NewGuid().ToString());

        Page<Testing.SampleEventItem> page2 = new(
            null,
            5,
            _events,
            0,
            Guid.NewGuid().ToString());

        Page<Testing.SampleEventItem> page3 = new(
            null,
            5,
            _events.Take(2).ToList(),
            0);

        _repository
            .SetupSequence(o => o.PageAsync(
                x => x.PartitionKey == Pk,
                5,
                It.IsAny<string>(),
                false,
                default))
            .ReturnsAsync(page1)
            .ReturnsAsync(page2)
            .ReturnsAsync(page3);

        List<Testing.SampleEventItem> events = new();

        //Act
        await foreach (Testing.SampleEventItem result in sut.StreamAsync(Pk, 5))
        {
            events.Add(result);
        }

        //Assert
        events.Count.Should().Be(13);
    }
}