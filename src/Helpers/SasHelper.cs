// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Aire.Memory.Helpers;

public static class SasHelper
{
    public static string GenerateContentUriString(BlobContainerClient container, string blobId)
    {
        var blobSasBuilder = new BlobSasBuilder()
        {
            ExpiresOn = DateTime.UtcNow.AddMinutes(60)
        };

        BlobClient blobClient = container.GetBlobClient(blobId);
        blobSasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(blobSasBuilder);
        return sasUri.AbsoluteUri;
    }
}
