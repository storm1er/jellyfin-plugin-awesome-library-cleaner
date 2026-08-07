using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AwesomeLibraryCleaner.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AwesomeLibraryCleaner.Services;

/// <summary>
/// Implementation of the library cleanup service.
/// </summary>
public class LibraryCleanupService : ILibraryCleanupService
{
    private readonly ILogger<LibraryCleanupService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ICollectionManager _collectionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryCleanupService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{LibraryCleanupService}"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    public LibraryCleanupService(
        ILogger<LibraryCleanupService> logger,
        ILibraryManager libraryManager,
        IUserManager userManager,
        ICollectionManager collectionManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _collectionManager = collectionManager;
    }

    /// <inheritdoc />
    public async Task ExecuteCleanupAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.LibrarySettings == null || config.LibrarySettings.Count == 0)
        {
            _logger.LogInformation("No library settings configured, skipping cleanup");
            return;
        }

        var enabledSettings = config.LibrarySettings.Where(s => s.Enabled).ToList();
        if (enabledSettings.Count == 0)
        {
            _logger.LogInformation("No enabled libraries, skipping cleanup");
            return;
        }

        _logger.LogInformation("Processing {Count} enabled libraries", enabledSettings.Count);

        for (int i = 0; i < enabledSettings.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var settings = enabledSettings[i];
            var progressPercent = (double)i / enabledSettings.Count * 100;
            progress?.Report(progressPercent);

            await ProcessLibraryAsync(settings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessLibraryAsync(LibrarySettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var library = _libraryManager.GetItemById(Guid.Parse(settings.LibraryId));
            if (library == null)
            {
                _logger.LogWarning("Library {LibraryId} not found", settings.LibraryId);
                return;
            }

            _logger.LogInformation("Processing library: {LibraryName}", library.Name);

            // Get all items in the library
            var items = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                Parent = library,
                Recursive = true,
                IncludeItemTypes = new[]
                {
                    Jellyfin.Data.Enums.BaseItemKind.Movie,
                    Jellyfin.Data.Enums.BaseItemKind.Episode,
                    Jellyfin.Data.Enums.BaseItemKind.Season,
                    Jellyfin.Data.Enums.BaseItemKind.Series
                },
                IsVirtualItem = false
            });

            if (items == null || items.Count == 0)
            {
                _logger.LogInformation("No items found in library {LibraryName}", library.Name);
                return;
            }

            _logger.LogInformation("Found {Count} items in library {LibraryName}", items.Count, library.Name);

            // Get user favorites to exclude
            var favoriteItemIds = new HashSet<Guid>();
            if (settings.ExcludeFavorites)
            {
                favoriteItemIds = await GetAllFavoriteItemIdsAsync().ConfigureAwait(false);
            }

            // The library manager loads user data onto returned items, so the latest play
            // dates can be computed without additional queries.
            var latestPlayedDates = settings.TimeReferenceOption == TimeReference.LatestWatchDate
                ? CleanupDecisionEngine.BuildLatestPlayedDates(items)
                : new Dictionary<Guid, DateTime?>();

            // Group items for processing
            var itemsToProcess = GetItemsToProcess(items, settings, favoriteItemIds);

            // Process leaving soon
            if (settings.LeavingSoonDays > 0)
            {
                await ProcessLeavingSoonAsync(library, settings, itemsToProcess, latestPlayedDates, cancellationToken).ConfigureAwait(false);
            }

            // Process deletions
            if (settings.DeletionDays > 0)
            {
                await ProcessDeletionsAsync(library, settings, itemsToProcess, latestPlayedDates, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing library {LibraryId}", settings.LibraryId);
        }
    }

    private List<BaseItem> GetItemsToProcess(IReadOnlyList<BaseItem> items, LibrarySettings settings, HashSet<Guid> favoriteItemIds)
    {
        var result = new List<BaseItem>();

        foreach (var item in items)
        {
            // Filter based on element reference for TV shows
            if (!CleanupDecisionEngine.ShouldProcessItem(item, settings.ElementReferenceOption))
            {
                _logger.LogDebug(
                    "Skip item {ItemName} - element reference is set to {ElementReference}",
                    item.Name,
                    settings.ElementReferenceOption);
                continue;
            }

            // Skip favorites if configured
            if (settings.ExcludeFavorites && IsFavorite(item, favoriteItemIds))
            {
                _logger.LogInformation("Excluding item {ItemName} - marked as favorite", item.Name);
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    private bool IsFavorite(BaseItem item, HashSet<Guid> favoriteItemIds)
    {
        // Check if item itself is a favorite
        if (favoriteItemIds.Contains(item.Id))
        {
            return true;
        }

        // For episodes and seasons, check if parent series is a favorite
        if (item is Episode episode && episode.Series != null && favoriteItemIds.Contains(episode.SeriesId))
        {
            return true;
        }

        if (item is Season season)
        {
            // Check if parent series is favorited
            if (season.Series != null && favoriteItemIds.Contains(season.SeriesId))
            {
                return true;
            }

            // Check if any episode in this season is favorited
            var episodes = season.GetRecursiveChildren().OfType<Episode>();
            if (episodes.Any(ep => favoriteItemIds.Contains(ep.Id)))
            {
                return true;
            }
        }

        // For series, check if any child season or episode is favorited
        if (item is Series series)
        {
            var children = series.GetRecursiveChildren();
            if (children.Any(child => favoriteItemIds.Contains(child.Id)))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<HashSet<Guid>> GetAllFavoriteItemIdsAsync()
    {
        var favoriteIds = new HashSet<Guid>();
        var users = _userManager.Users.ToList();

        foreach (var user in users)
        {
            var userFavorites = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                User = user,
                IsFavorite = true,
                Recursive = true
            });

            foreach (var fav in userFavorites)
            {
                favoriteIds.Add(fav.Id);
            }
        }

        return await Task.FromResult(favoriteIds).ConfigureAwait(false);
    }

    private async Task ProcessLeavingSoonAsync(
        BaseItem library,
        LibrarySettings settings,
        List<BaseItem> items,
        IReadOnlyDictionary<Guid, DateTime?> latestPlayedDates,
        CancellationToken cancellationToken)
    {
        try
        {
            var collectionName = $"{library.Name} - {settings.LeavingSoonCollectionName}";

            _logger.LogInformation("Updating 'Leaving Soon' collection: '{CollectionName}'...", collectionName);

            // Find items that should be in leaving soon
            var leavingSoonItems = new List<BaseItem>();
            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var latestPlayedDate = latestPlayedDates.TryGetValue(item.Id, out var date) ? date : null;
                var referenceDate = CleanupDecisionEngine.GetReferenceDate(item, settings.TimeReferenceOption, latestPlayedDate);
                var daysSinceReference = CleanupDecisionEngine.GetDaysSinceReference(item, settings.TimeReferenceOption, latestPlayedDate);
                var shouldBeInLeavingSoon = CleanupDecisionEngine.ShouldBeInLeavingSoon(item, settings, latestPlayedDate);

                if (shouldBeInLeavingSoon)
                {
                    leavingSoonItems.Add(item);
                }

                LogItemDateInfo(item, settings, referenceDate, daysSinceReference, shouldBeInLeavingSoon ? "SendToLeavingSoon" : "NotLeavingSoon", latestPlayedDate);
            }

            _logger.LogInformation("Found {Count} items for leaving soon", leavingSoonItems.Count);

            var desiredItemIds = leavingSoonItems.Select(i => i.Id).ToHashSet().ToList();
            await UpdateCollectionAsync(collectionName, desiredItemIds, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Leaving soon collection updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing leaving soon for library {LibraryName}", library.Name);
        }
    }

    private async Task ProcessDeletionsAsync(
        BaseItem library,
        LibrarySettings settings,
        List<BaseItem> items,
        IReadOnlyDictionary<Guid, DateTime?> latestPlayedDates,
        CancellationToken cancellationToken)
    {
        try
        {
            var itemsToDelete = new List<BaseItem>();
            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var latestPlayedDate = latestPlayedDates.TryGetValue(item.Id, out var date) ? date : null;
                var referenceDate = CleanupDecisionEngine.GetReferenceDate(item, settings.TimeReferenceOption, latestPlayedDate);
                var daysSinceReference = CleanupDecisionEngine.GetDaysSinceReference(item, settings.TimeReferenceOption, latestPlayedDate);
                var shouldBeDeleted = CleanupDecisionEngine.ShouldBeDeleted(item, settings, latestPlayedDate);

                if (shouldBeDeleted)
                {
                    itemsToDelete.Add(item);
                }

                LogItemDateInfo(item, settings, referenceDate, daysSinceReference, shouldBeDeleted ? "SendToDelete" : "NotToDelete", latestPlayedDate);
            }

            _logger.LogInformation("Found {Count} items to delete", itemsToDelete.Count);

            if (settings.DeleteAutomation)
            {
                // Automated deletion
                foreach (var item in itemsToDelete)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await DeleteItemAsync(item).ConfigureAwait(false);
                }
            }
            else
            {
                // Add to "To Delete" collection for manual review
                var collectionName = $"{library.Name} - To Delete";

                _logger.LogInformation("Updating 'To Delete' collection: '{CollectionName}'...", collectionName);

                // Clean up existing collection
                await RemoveCollectionAsync(collectionName).ConfigureAwait(false);

                // If no items to delete, stop here
                if (itemsToDelete.Count == 0)
                {
                    return;
                }

                // Get or create the to delete collection
                var collection = await CreateCollectionAsync(collectionName).ConfigureAwait(false);

                if (collection == null)
                {
                    _logger.LogError("Failed to get or create collection {CollectionName}", collectionName);
                    return;
                }

                var targetItemIds = itemsToDelete.Select(i => i.Id).ToHashSet().ToList();
                await _collectionManager.AddToCollectionAsync(collection.Id, targetItemIds).ConfigureAwait(false);
                _logger.LogInformation("Added {Count} items to to delete collection", targetItemIds.Count);
                _logger.LogInformation("To delete collection updated successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing deletions for library {LibraryName}", library.Name);
        }
    }

    private async Task<BoxSet?> CreateCollectionAsync(string collectionName)
    {
        try
        {
            _logger.LogInformation("Creating new collection: {CollectionName}", collectionName);

            var createdCollection = await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = collectionName,
                IsLocked = false
            }).ConfigureAwait(false);

            return createdCollection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating collection: {CollectionName}", collectionName);
            return null;
        }
    }

    private async Task RemoveCollectionAsync(string collectionName)
    {
        try
        {
            // Try to find existing collection
            var existingCollections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = false
            }).OfType<BoxSet>().Where(c => c.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));

            var collection = existingCollections.FirstOrDefault();

            if (collection == null)
            {
                _logger.LogInformation("Collection {CollectionName} does not exist, nothing to remove", collectionName);
                return;
            }

            // Delete the collection
            _logger.LogInformation("Deleting collection: {CollectionName}", collectionName);
            _libraryManager.DeleteItem(
                collection,
                new MediaBrowser.Controller.Library.DeleteOptions
                {
                    DeleteFileLocation = true
                });

            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing collection: {CollectionName}", collectionName);
        }
    }

    private void LogItemDateInfo(BaseItem item, LibrarySettings settings, DateTime usedReferenceDate, double daysSinceReference, string status, DateTime? latestPlayedDate)
    {
        // Get all available dates
        var fileAddedDate = item.DateCreated;
        var fileModifiedDate = item.DateModified;

        // Calculate days for each date
        var daysSinceAdded = (DateTime.UtcNow - fileAddedDate).TotalDays;
        var daysSinceModified = (DateTime.UtcNow - fileModifiedDate).TotalDays;
        var daysSinceWatched = latestPlayedDate.HasValue ? (DateTime.UtcNow - latestPlayedDate.Value).TotalDays : (double?)null;

        // Determine which reference is configured
        var configuredReference = settings.TimeReferenceOption switch
        {
            TimeReference.FileAddedDate => "FileAddedDate",
            TimeReference.LatestFileUpdateDate => "LatestFileUpdateDate",
            TimeReference.LatestWatchDate => "LatestWatchDate",
            _ => "Unknown"
        };

        _logger.LogDebug(
            "Status: {Status} | " +
            "ConfiguredReference: {ConfiguredReference} ({UsedDate:yyyy-MM-dd}, {DaysSince:F1} days) | " +
            "FileAddedDate: {FileAddedDate:yyyy-MM-dd} ({DaysSinceAdded:F1} days) | " +
            "FileModifiedDate: {FileModifiedDate:yyyy-MM-dd} ({DaysSinceModified:F1} days) | " +
            "LastWatchDate: {LastWatchDate:yyyy-MM-dd} ({DaysSinceWatched} days) | " +
            "LeavingSoonThreshold: {LeavingSoonDays} days | DeletionThreshold: {DeletionDays} days | " +
            "Item: {ItemName}",
            status,
            configuredReference,
            usedReferenceDate,
            daysSinceReference,
            fileAddedDate,
            daysSinceAdded,
            fileModifiedDate,
            daysSinceModified,
            latestPlayedDate,
            daysSinceWatched.HasValue ? daysSinceWatched.Value.ToString("F1", CultureInfo.InvariantCulture) : "Never",
            settings.LeavingSoonDays,
            settings.DeletionDays,
            item.Name);
    }

    private async Task DeleteItemAsync(BaseItem item)
    {
        try
        {
            var path = item.Path;
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Cannot delete item {ItemName}: path is empty", item.Name);
                return;
            }

            _logger.LogInformation("Deleting item: {ItemName} at {Path}", item.Name, path);

            _libraryManager.DeleteItem(
                item,
                new MediaBrowser.Controller.Library.DeleteOptions
                {
                    DeleteFileLocation = true
                });

            await Task.CompletedTask.ConfigureAwait(false);

            _logger.LogInformation("Successfully deleted item: {ItemName}", item.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting item {ItemName}", item.Name);
        }
    }
}
