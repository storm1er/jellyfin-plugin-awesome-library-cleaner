using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.AwesomeLibraryCleaner.Tests;

/// <summary>
/// Factory helpers for creating test entities without invoking constructors that
/// require a runtime (Jellyfin server) environment.
/// </summary>
public static class TestItemFactory
{
    /// <summary>
    /// Creates an instance of the given type without running its constructor.
    /// </summary>
    public static T Create<T>()
        where T : BaseItem
    {
#pragma warning disable SYSLIB0050
        var item = (T)FormatterServices.GetUninitializedObject(typeof(T));
#pragma warning restore SYSLIB0050
        item.Id = Guid.NewGuid();
        item.Name = typeof(T).Name;
        item.DateCreated = DateTime.UtcNow;
        item.DateModified = DateTime.UtcNow;
        item.UserData = new List<UserData>();
        return item;
    }

    /// <summary>
    /// Creates a user data record for an item with an optional last played date.
    /// </summary>
    public static UserData UserDataFor(Guid itemId, DateTime? playDate)
    {
        return new UserData
        {
            ItemId = itemId,
            CustomDataKey = "test-user",
            UserId = Guid.NewGuid(),
            Item = null,
            User = null,
            LastPlayedDate = playDate
        };
    }

    /// <summary>
    /// Creates a movie with the given play dates (one entry per user).
    /// </summary>
    public static Movie CreateMovie(params DateTime?[] playDates)
    {
        var movie = Create<Movie>();
        foreach (var date in playDates)
        {
            movie.UserData.Add(UserDataFor(movie.Id, date));
        }

        return movie;
    }

    /// <summary>
    /// Creates an episode linked to the given season and series ids.
    /// </summary>
    public static Episode CreateEpisode(Guid seasonId, Guid seriesId, DateTime? playDate)
    {
        var episode = Create<Episode>();
        episode.SeasonId = seasonId;
        episode.SeriesId = seriesId;
        if (playDate.HasValue)
        {
            episode.UserData.Add(UserDataFor(episode.Id, playDate));
        }

        return episode;
    }

    /// <summary>
    /// Creates a season linked to the given series id.
    /// </summary>
    public static Season CreateSeason(Guid seriesId)
    {
        var season = Create<Season>();
        season.SeriesId = seriesId;
        return season;
    }

    /// <summary>
    /// Creates a series.
    /// </summary>
    public static Series CreateSeries()
    {
        return Create<Series>();
    }
}
