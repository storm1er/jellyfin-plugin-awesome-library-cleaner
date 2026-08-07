using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AwesomeLibraryCleaner.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.AwesomeLibraryCleaner.Services;

/// <summary>
/// Pure decision logic used to determine whether an item should be moved to the
/// "Leaving Soon" or "To Delete" collections. Kept free of I/O so it can be unit tested.
/// </summary>
public static class CleanupDecisionEngine
{
    /// <summary>
    /// Determines whether an item participates in cleanup based on the configured element granularity.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="elementReference">The configured element reference for TV shows.</param>
    /// <returns><c>true</c> if the item is eligible for cleanup.</returns>
    public static bool ShouldProcessItem(BaseItem item, ElementReference elementReference)
    {
        return item switch
        {
            Movie => true,
            Episode => elementReference == ElementReference.Episode,
            Season => elementReference == ElementReference.Season,
            Series => elementReference == ElementReference.WholeSeries,
            _ => false
        };
    }

    /// <summary>
    /// Determines whether an item should be moved to the "Leaving Soon" collection.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="settings">The library settings.</param>
    /// <param name="latestPlayedDate">The latest play date across all users, or <c>null</c> if never played.</param>
    /// <returns><c>true</c> if the item should be added to the leaving soon collection.</returns>
    public static bool ShouldBeInLeavingSoon(BaseItem item, LibrarySettings settings, DateTime? latestPlayedDate)
    {
        if (settings.LeavingSoonDays <= 0)
        {
            return false;
        }

        var daysSinceReference = GetDaysSinceReference(item, settings.TimeReferenceOption, latestPlayedDate);
        var belowDeletionThreshold = settings.DeletionDays <= 0 || daysSinceReference < settings.DeletionDays;

        return daysSinceReference >= settings.LeavingSoonDays && belowDeletionThreshold;
    }

    /// <summary>
    /// Determines whether an item should be deleted.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="settings">The library settings.</param>
    /// <param name="latestPlayedDate">The latest play date across all users, or <c>null</c> if never played.</param>
    /// <returns><c>true</c> if the item should be deleted.</returns>
    public static bool ShouldBeDeleted(BaseItem item, LibrarySettings settings, DateTime? latestPlayedDate)
    {
        if (settings.DeletionDays <= 0)
        {
            return false;
        }

        var daysSinceReference = GetDaysSinceReference(item, settings.TimeReferenceOption, latestPlayedDate);
        return daysSinceReference >= settings.DeletionDays;
    }

    /// <summary>
    /// Gets the reference date used for age calculations.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="timeReference">The configured time reference.</param>
    /// <param name="latestPlayedDate">The latest play date across all users, or <c>null</c> if never played.</param>
    /// <returns>The reference date. For the <see cref="TimeReference.LatestWatchDate"/> option, falls back to
    /// <see cref="BaseItem.DateCreated"/> when the item has never been played.</returns>
    public static DateTime GetReferenceDate(BaseItem item, TimeReference timeReference, DateTime? latestPlayedDate)
    {
        return timeReference switch
        {
            TimeReference.FileAddedDate => item.DateCreated,
            TimeReference.LatestFileUpdateDate => item.DateModified,
            TimeReference.LatestWatchDate => latestPlayedDate ?? item.DateCreated,
            _ => item.DateCreated
        };
    }

    /// <summary>
    /// Gets the number of days since the configured reference date.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <param name="timeReference">The configured time reference.</param>
    /// <param name="latestPlayedDate">The latest play date across all users, or <c>null</c> if never played.</param>
    /// <returns>The number of days since the reference date.</returns>
    public static double GetDaysSinceReference(BaseItem item, TimeReference timeReference, DateTime? latestPlayedDate)
    {
        return (DateTime.UtcNow - GetReferenceDate(item, timeReference, latestPlayedDate)).TotalDays;
    }

    /// <summary>
    /// Builds a map of item id to the latest play date across all users, propagating
    /// episode play dates up to their season and series so that folder-level cleanup
    /// decisions reflect actual viewing activity.
    /// </summary>
    /// <param name="items">The items to evaluate.</param>
    /// <returns>A map of item id to latest play date, or <c>null</c> for never played items.</returns>
    public static IReadOnlyDictionary<Guid, DateTime?> BuildLatestPlayedDates(IReadOnlyCollection<BaseItem> items)
    {
        var result = new Dictionary<Guid, DateTime?>();
        var episodes = new List<Episode>();

        foreach (var item in items)
        {
            var ownDate = GetOwnLatestPlayedDate(item);
            Update(result, item.Id, ownDate);

            if (item is Episode episode)
            {
                episodes.Add(episode);
            }
        }

        foreach (var episode in episodes)
        {
            var episodeDate = result.TryGetValue(episode.Id, out var date) ? date : null;
            Update(result, episode.SeasonId, episodeDate);
            Update(result, episode.SeriesId, episodeDate);
        }

        foreach (var item in items)
        {
            if (item is Season season && result.TryGetValue(season.Id, out var seasonDate))
            {
                Update(result, season.SeriesId, seasonDate);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the latest play date for an item across all users, using the user data that is
    /// loaded onto the item by the library manager.
    /// </summary>
    /// <param name="item">The item to evaluate.</param>
    /// <returns>The latest play date, or <c>null</c> if the item has never been played.</returns>
    public static DateTime? GetOwnLatestPlayedDate(BaseItem item)
    {
        DateTime? max = null;

        foreach (var userData in item.UserData)
        {
            if (userData.LastPlayedDate.HasValue && (max is null || userData.LastPlayedDate > max))
            {
                max = userData.LastPlayedDate;
            }
        }

        return max;
    }

    private static void Update(Dictionary<Guid, DateTime?> dates, Guid itemId, DateTime? date)
    {
        if (!date.HasValue)
        {
            return;
        }

        if (!dates.TryGetValue(itemId, out var current) || current is null || date > current)
        {
            dates[itemId] = date;
        }
    }
}
