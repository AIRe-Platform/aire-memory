// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Aire.Memory.Helpers;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;

public static class BlobHelper
{
    // Generate the Thumbnail URL if the blob exists
    public static async Task<string> GenerateThumbnailUrlIfExists(BlobContainerClient blobContainer, string contentId)
    {
        var thumbnailBlobClient = blobContainer.GetBlobClient($"{contentId}/thumbnail");
        var thumbnailExists = await thumbnailBlobClient.ExistsAsync();
        if (thumbnailExists)
        {
            return SasHelper.GenerateContentUriString(blobContainer, $"{contentId}/thumbnail");
        }
        return "";
    }

    // Upload the thumbnail if provided and return the URL, otherwise return null
    public static async Task<string> UploadThumbnailIfPresent(IFormFileCollection files, BlobContainerClient blobContainer, string contentId)
    {
        if (files.Any(f => f.Name == "thumbnail"))
        {
            var thumbnailFile = files.First(f => f.Name == "thumbnail");
            var thumbnailBlobClient = blobContainer.GetBlobClient($"{contentId}/thumbnail");

            using var thumbnailStream = thumbnailFile.OpenReadStream();
            var thumbnailHttpHeader = new BlobHttpHeaders { ContentType = thumbnailFile.ContentType };

            await thumbnailBlobClient.UploadAsync(thumbnailStream, new BlobUploadOptions { HttpHeaders = thumbnailHttpHeader });
            return thumbnailBlobClient.Uri.ToString(); // Return the thumbnail URL
        }

        return "";
    }

    // Optionally remove the thumbnail if the blob exists
    public static async Task RemoveThumbnailIfExists(BlobContainerClient blobContainer, string contentId)
    {
        var thumbnailBlobClient = blobContainer.GetBlobClient($"{contentId}/thumbnail");
        var thumbnailExists = await thumbnailBlobClient.ExistsAsync();
        if (thumbnailExists)
        {
            await thumbnailBlobClient.DeleteIfExistsAsync();
        }
    }

     // Uploads a file to the blob storage and returns the URL
    public static async Task<string> UploadBlobAsync(IFormFile file, BlobContainerClient blobContainer, string contentId)
    {
        var blobClient = blobContainer.GetBlobClient(contentId);
        using var stream = file.OpenReadStream();
        var blobHttpHeader = new BlobHttpHeaders { ContentType = file.ContentType };

        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeader });
        return blobClient.Uri.ToString();
    }
}
