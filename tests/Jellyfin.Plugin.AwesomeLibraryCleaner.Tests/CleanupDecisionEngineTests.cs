using System;
using Jellyfin.Plugin.AwesomeLibraryCleaner.Configuration;
using Jellyfin.Plugin.AwesomeLibraryCleaner.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.AwesomeLibraryCleaner.Tests;

/// <summary>
/// Unit tests for <see cref="CleanupDecisionEngine"/>.
/// </summary>
public class CleanupDecisionEngineTests
{
    private static LibrarySettings CreateSettings(
        TimeReference timeReference = TimeReference.FileAddedDate,
        int leavingSoonDays = 0,
        int deletionDays = 0,
        ElementReference elementReference = ElementReference.WholeSeries)
    {
        return new LibrarySettings
        {
            Enabled = true,
            TimeReferenceOption = timeReference,
            LeavingSoonDays = leavingSoonDays,
            DeletionDays = deletionDays,
            ElementReferenceOption = elementReference
        };
    }

    [Theory]
    [InlineData(ElementReference.Episode)]
    [InlineData(ElementReference.Season)]
    [InlineData(ElementReference.WholeSeries)]
    public void ShouldProcessItem_Movie_AlwaysProcessed(ElementReference elementReference)
    {
        var movie = TestItemFactory.CreateMovie();

        Assert.True(CleanupDecisionEngine.ShouldProcessItem(movie, elementReference));
    }

    [Theory]
    [InlineData(ElementReference.Episode, true)]
    [InlineData(ElementReference.Season, false)]
    [InlineData(ElementReference.WholeSeries, false)]
    public void ShouldProcessItem_Episode_OnlyForEpisodeReference(ElementReference elementReference, bool expected)
    {
        var episode = TestItemFactory.CreateEpisode(Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.Equal(expected, CleanupDecisionEngine.ShouldProcessItem(episode, elementReference));
    }

    [Theory]
    [InlineData(ElementReference.Episode, false)]
    [InlineData(ElementReference.Season, true)]
    [InlineData(ElementReference.WholeSeries, false)]
    public void ShouldProcessItem_Season_OnlyForSeasonReference(ElementReference elementReference, bool expected)
    {
        var season = TestItemFactory.CreateSeason(Guid.NewGuid());

        Assert.Equal(expected, CleanupDecisionEngine.ShouldProcessItem(season, elementReference));
    }

    [Theory]
    [InlineData(ElementReference.Episode, false)]
    [InlineData(ElementReference.Season, false)]
    [InlineData(ElementReference.WholeSeries, true)]
    public void ShouldProcessItem_Series_OnlyForWholeSeriesReference(ElementReference elementReference, bool expected)
    {
        var series = TestItemFactory.CreateSeries();

        Assert.Equal(expected, CleanupDecisionEngine.ShouldProcessItem(series, elementReference));
    }

    [Fact]
    public void ShouldProcessItem_OtherItemTypes_AreIgnored()
    {
        var boxSet = TestItemFactory.Create<MediaBrowser.Controller.Entities.Movies.BoxSet>();

        Assert.False(CleanupDecisionEngine.ShouldProcessItem(boxSet, ElementReference.WholeSeries));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_DisabledWhenLeavingSoonDaysZero()
    {
        var movie = TestItemFactory.CreateMovie(DateTime.UtcNow.AddDays(-100));

        var settings = CreateSettings(leavingSoonDays: 0, deletionDays: 30);

        Assert.False(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_True_WhenAboveThreshold_AndDeletionDisabled()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-10);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, leavingSoonDays: 5, deletionDays: 0);

        Assert.True(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_True_WhenAboveLeavingButBelowDeletion()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-10);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, leavingSoonDays: 5, deletionDays: 30);

        Assert.True(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_False_WhenPastDeletionThreshold()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-50);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, leavingSoonDays: 5, deletionDays: 30);

        Assert.False(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_False_WhenBelowLeavingThreshold()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-2);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, leavingSoonDays: 5, deletionDays: 0);

        Assert.False(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void ShouldBeDeleted_DisabledWhenDeletionDaysZero()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-100);

        var settings = CreateSettings(deletionDays: 0);

        Assert.False(CleanupDecisionEngine.ShouldBeDeleted(movie, settings, null));
    }

    [Fact]
    public void ShouldBeDeleted_True_WhenPastDeletionThreshold()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-50);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, deletionDays: 30);

        Assert.True(CleanupDecisionEngine.ShouldBeDeleted(movie, settings, null));
    }

    [Fact]
    public void ShouldBeDeleted_False_WhenBelowDeletionThreshold()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-10);

        var settings = CreateSettings(timeReference: TimeReference.FileAddedDate, deletionDays: 30);

        Assert.False(CleanupDecisionEngine.ShouldBeDeleted(movie, settings, null));
    }

    [Fact]
    public void GetReferenceDate_FileAddedDate_UsesDateCreated()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        movie.DateModified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(movie.DateCreated, CleanupDecisionEngine.GetReferenceDate(movie, TimeReference.FileAddedDate, null));
    }

    [Fact]
    public void GetReferenceDate_LatestFileUpdateDate_UsesDateModified()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        movie.DateModified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(movie.DateModified, CleanupDecisionEngine.GetReferenceDate(movie, TimeReference.LatestFileUpdateDate, null));
    }

    [Fact]
    public void GetReferenceDate_LatestWatchDate_UsesLatestPlayedDate()
    {
        var movie = TestItemFactory.CreateMovie();
        var played = new DateTime(2023, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(played, CleanupDecisionEngine.GetReferenceDate(movie, TimeReference.LatestWatchDate, played));
    }

    [Fact]
    public void GetReferenceDate_LatestWatchDate_NeverPlayed_FallsBackToDateCreated()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(movie.DateCreated, CleanupDecisionEngine.GetReferenceDate(movie, TimeReference.LatestWatchDate, null));
    }

    [Fact]
    public void ShouldBeDeleted_LatestWatchDate_NeverPlayed_UsesDateCreatedFallback()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-50);

        var settings = CreateSettings(timeReference: TimeReference.LatestWatchDate, deletionDays: 30);

        Assert.True(CleanupDecisionEngine.ShouldBeDeleted(movie, settings, null));
    }

    [Fact]
    public void ShouldBeInLeavingSoon_LatestWatchDate_NeverPlayed_UsesDateCreatedFallback()
    {
        var movie = TestItemFactory.CreateMovie();
        movie.DateCreated = DateTime.UtcNow.AddDays(-10);

        var settings = CreateSettings(timeReference: TimeReference.LatestWatchDate, leavingSoonDays: 5, deletionDays: 30);

        Assert.True(CleanupDecisionEngine.ShouldBeInLeavingSoon(movie, settings, null));
    }

    [Fact]
    public void BuildLatestPlayedDates_Movie_UsesOwnUserData()
    {
        var played = new DateTime(2023, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var movie = TestItemFactory.CreateMovie(played);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new[] { movie });

        Assert.Equal(played, result[movie.Id]);
    }

    [Fact]
    public void BuildLatestPlayedDates_Movie_NeverPlayed_IsNull()
    {
        var movie = TestItemFactory.CreateMovie((DateTime?)null);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new[] { movie });

        Assert.False(result.TryGetValue(movie.Id, out var date));
        Assert.Null(date);
    }

    [Fact]
    public void BuildLatestPlayedDates_TakesMaxAcrossUsers()
    {
        var older = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var movie = TestItemFactory.CreateMovie(older, newer);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new[] { movie });

        Assert.Equal(newer, result[movie.Id]);
    }

    [Fact]
    public void BuildLatestPlayedDates_PropagatesEpisodeToSeasonAndSeries()
    {
        var series = TestItemFactory.CreateSeries();
        var season = TestItemFactory.CreateSeason(series.Id);
        var played = new DateTime(2023, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        var episode = TestItemFactory.CreateEpisode(season.Id, series.Id, played);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new BaseItem[] { series, season, episode });

        Assert.Equal(played, result[episode.Id]);
        Assert.Equal(played, result[season.Id]);
        Assert.Equal(played, result[series.Id]);
    }

    [Fact]
    public void BuildLatestPlayedDates_PropagatesSeasonToSeries()
    {
        var series = TestItemFactory.CreateSeries();
        var season = TestItemFactory.CreateSeason(series.Id);
        var played = new DateTime(2023, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        season.UserData.Add(TestItemFactory.UserDataFor(season.Id, played));

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new BaseItem[] { series, season });

        Assert.Equal(played, result[season.Id]);
        Assert.Equal(played, result[series.Id]);
    }

    [Fact]
    public void BuildLatestPlayedDates_NeverPlayedEpisode_DoesNotMarkSeriesPlayed()
    {
        var series = TestItemFactory.CreateSeries();
        var season = TestItemFactory.CreateSeason(series.Id);
        var episode = TestItemFactory.CreateEpisode(season.Id, series.Id, null);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new BaseItem[] { series, season, episode });

        Assert.False(result.TryGetValue(episode.Id, out var episodeDate));
        Assert.False(result.TryGetValue(season.Id, out var seasonDate));
        Assert.False(result.TryGetValue(series.Id, out var seriesDate));
        Assert.Null(episodeDate);
        Assert.Null(seasonDate);
        Assert.Null(seriesDate);
    }

    [Fact]
    public void BuildLatestPlayedDates_PropagatesLatestEpisodeOnly()
    {
        var series = TestItemFactory.CreateSeries();
        var season = TestItemFactory.CreateSeason(series.Id);
        var oldPlayed = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newPlayed = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldEpisode = TestItemFactory.CreateEpisode(season.Id, series.Id, oldPlayed);
        var newEpisode = TestItemFactory.CreateEpisode(season.Id, series.Id, newPlayed);

        var result = CleanupDecisionEngine.BuildLatestPlayedDates(new BaseItem[] { series, season, oldEpisode, newEpisode });

        Assert.Equal(newPlayed, result[season.Id]);
        Assert.Equal(newPlayed, result[series.Id]);
    }
}
