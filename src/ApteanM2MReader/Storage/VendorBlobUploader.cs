using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace JetSolutions.ApteanM2MReader.Storage;

public sealed class VendorBlobUploader(BlobServiceClient blobServiceClient)
{
    public async Task<Uri> UploadAsync(
        string containerName,
        string blobName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var blob = blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        await using var stream = File.OpenRead(filePath);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                }
            },
            cancellationToken);

        return blob.Uri;
    }
}
