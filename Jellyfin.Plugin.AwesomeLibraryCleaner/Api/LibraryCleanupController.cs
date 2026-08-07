using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AwesomeLibraryCleaner.Api;

/// <summary>
/// Controller for library cleanup operations.
/// </summary>
[Authorize(Policy = "RequiresElevation")]
[ApiController]
[Route("AwesomeLibraryCleaner")]
[Produces("application/json")]
public class LibraryCleanupController : ControllerBase
{
    private readonly ILogger<LibraryCleanupController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryCleanupController"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{LibraryCleanupController}"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    public LibraryCleanupController(
        ILogger<LibraryCleanupController> logger,
        ILibraryManager libraryManager,
        ICollectionManager collectionManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
    }

    /// <summary>
    /// Gets pending deletions from all "To Delete" collections.
    /// </summary>
    /// <returns>A <see cref="PendingDeletionsResponse"/> containing pending deletion information.</returns>
    [HttpGet("PendingDeletions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PendingDeletionsResponse> GetPendingDeletions()
    {
        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config?.LibrarySettings == null)
            {
                return Ok(new PendingDeletionsResponse { Libraries = new List<LibraryDeletionInfo>() });
            }

            var response = new PendingDeletionsResponse
            {
                Libraries = new List<LibraryDeletionInfo>()
            };

            foreach (var settings in config.LibrarySettings.Where(s => s.Enabled && !s.DeleteAutomation))
            {
                var library = _libraryManager.GetItemById(Guid.Parse(settings.LibraryId));
                if (library == null)
                {
                    continue;
                }

                var collectionName = $"{library.Name} - To Delete";

                // Find the "To Delete" collection
                var collection = FindCollectionByName(collectionName);
                if (collection == null)
                {
                    continue;
                }

                // Get items in the collection
                var collectionItems = collection.GetRecursiveChildren().ToList();

                if (collectionItems.Count > 0)
                {
                    var items = collectionItems.Select(item => new MediaItemInfo
                    {
                        Name = item.Name,
                        ItemId = item.Id.ToString(),
                        Path = item.Path,
                        DateCreated = item.DateCreated,
                        DateModified = item.DateModified,
                        LastPlayedDate = Services.CleanupDecisionEngine.GetOwnLatestPlayedDate(item)
                    }).ToList();

                    response.Libraries.Add(new LibraryDeletionInfo
                    {
                        LibraryId = settings.LibraryId,
                        LibraryName = library.Name,
                        CollectionId = collection.Id.ToString(),
                        Items = items
                    });
                }
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending deletions");
            return StatusCode(500, "An error occurred while retrieving pending deletions");
        }
    }

    /// <summary>
    /// Deletes specified items.
    /// </summary>
    /// <param name="request">The deletion request containing item IDs to delete.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [HttpPost("Delete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteItems([FromBody] DeleteItemsRequest request)
    {
        if (request?.ItemIds == null || request.ItemIds.Count == 0)
        {
            return BadRequest("No item IDs specified");
        }

        try
        {
            _logger.LogInformation("Deleting {Count} items", request.ItemIds.Count);

            foreach (var itemIdStr in request.ItemIds)
            {
                if (!Guid.TryParse(itemIdStr, out var itemId))
                {
                    _logger.LogWarning("Invalid item ID: {ItemId}", itemIdStr);
                    continue;
                }

                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    _logger.LogWarning("Item not found: {ItemId}", itemId);
                    continue;
                }

                await DeleteItemAsync(item).ConfigureAwait(false);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting items");
            return StatusCode(500, "An error occurred while deleting items");
        }
    }

    private BoxSet? FindCollectionByName(string collectionName)
    {
        try
        {
            var collections = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = false
            }).OfType<BoxSet>().Where(c => c.Name.Equals(collectionName, StringComparison.OrdinalIgnoreCase));

            return collections.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding collection: {CollectionName}", collectionName);
            return null;
        }
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

            // Delete from library (Jellyfin handles file deletion when DeleteFileLocation is true)
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
            _logger.LogError(ex, "Error deleting item: {ItemName}", item.Name);
            throw;
        }
    }
}

/// <summary>
/// Response model for pending deletions.
/// </summary>
public class PendingDeletionsResponse
{
    /// <summary>
    /// Gets or sets the list of libraries with pending deletions.
    /// </summary>
    public List<LibraryDeletionInfo> Libraries { get; set; } = new List<LibraryDeletionInfo>();
}

/// <summary>
/// Information about a library with pending deletions.
/// </summary>
public class LibraryDeletionInfo
{
    /// <summary>
    /// Gets or sets the library ID.
    /// </summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library name.
    /// </summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection ID.
    /// </summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of items pending deletion.
    /// </summary>
    public List<MediaItemInfo> Items { get; set; } = new List<MediaItemInfo>();
}

/// <summary>
/// Information about a media item.
/// </summary>
public class MediaItemInfo
{
    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date the item was created (file added date).
    /// </summary>
    public DateTime DateCreated { get; set; }

    /// <summary>
    /// Gets or sets the date the item was last modified.
    /// </summary>
    public DateTime DateModified { get; set; }

    /// <summary>
    /// Gets or sets the date the item was last watched by any user, or <c>null</c> if never watched.
    /// </summary>
    public DateTime? LastPlayedDate { get; set; }
}

/// <summary>
/// Request model for deleting items.
/// </summary>
public class DeleteItemsRequest
{
    /// <summary>
    /// Gets or sets the list of item IDs to delete.
    /// </summary>
    public List<string> ItemIds { get; set; } = new List<string>();
}
