// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Http;

using ContentType = Aire.Sdk.Models.Resources.ContentType;

namespace Aire.Memory.Helpers;

public static class ContentHelper
{
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

    public static bool IsValidContentUrl(string url)
    {
        string[] allowedSchemes = ["http", "https"];
        try
        {
            var uri = new UriBuilder(url);

            if (!allowedSchemes.Contains(uri.Scheme))
                return false;

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
