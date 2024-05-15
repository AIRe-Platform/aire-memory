using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Aire.Memory.Helpers;

public static class SasHelper
{
    public static string GenerateContentUriString(BlobContainerClient container, string blobId)
    {
        var blobSasBuilder = new BlobSasBuilder()
        {
            ExpiresOn = DateTime.UtcNow.AddMinutes(15)
        };

        BlobClient blobClient = container.GetBlobClient(blobId);
        blobSasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(blobSasBuilder);
        return sasUri.AbsoluteUri;
    }
}
