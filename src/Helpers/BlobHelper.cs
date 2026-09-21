// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Aire.Sdk.Models.Resources;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using System.Net.Mime;

using ContentType = Aire.Sdk.Models.Resources.ContentType;

namespace Aire.Memory.Helpers;

public static class BlobHelper
{
    // Generate the Thumbnail URL if the blob exists
    public static async Task<string> GenerateThumbnailUrlIfExists(BlobContainerClient blobContainer, string contentId)
    {
        var thumbnailBlobClient = blobContainer.GetBlobClient($"{contentId}/thumbnail");
        var thumbnailExists = await thumbnailBlobClient.ExistsAsync();
        if (thumbnailExists)
        {
            return SasHelper.GenerateSasUriString(blobContainer, $"{contentId}/thumbnail");
        }
        return "";
    }

    // Upload the thumbnail if provided and return the URL, otherwise return null
    public static async Task<string> UploadThumbnail(IFormFile file, BlobContainerClient blobContainer, string contentId)
    {
        var thumbnailid = $"{contentId}/thumbnail";
        return await UploadBlobAsync(file, blobContainer, thumbnailid);
    }

    // Optionally remove the thumbnail if the blob exists
    public static async Task RemoveThumbnailIfExists(BlobContainerClient blobContainer, string contentId)
    {
        var thumbnailBlobClient = blobContainer.GetBlobClient($"{contentId}/thumbnail");
        await thumbnailBlobClient.DeleteIfExistsAsync();
    }

    // Uploads a file to the blob storage and returns the URL
    public static async Task<string> UploadBlobAsync(IFormFile file, BlobContainerClient blobContainer, string blobId)
    {
        using var stream = file.OpenReadStream();
        return await UploadBlobAsync(stream, file.ContentType, blobContainer, blobId);
    }

    // Uploads a file to the blob storage and returns the URL
    public static async Task<string> UploadBlobAsync(Stream stream, string contentType, BlobContainerClient blobContainer, string blobId)
    {
        var blobClient = blobContainer.GetBlobClient(blobId);
        var blobHttpHeader = new BlobHttpHeaders { ContentType = contentType };
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeader });
        return blobClient.Uri.ToString();
    }

    public static bool IsValidThumbnailContentType(IFormFile file)
    {
        return IsValidContentType(ContentType.Image, file);
    }

    public static bool IsValidContentType(ContentType type, IFormFile file)
    {
        if (type == ContentType.URL)
            return false;

        if (type == ContentType.Image)
        {
            return file.ContentType.StartsWith("image/");
        }

        if (type == ContentType.Video)
        {
            return file.ContentType.StartsWith("video/");
        }

        if (type == ContentType.Document)
        {
            string[] docTypes = [
                "text/markdown",
                "text/plain",
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            ];
            return docTypes.Contains(file.ContentType);
        }

        return false;
    }
}
