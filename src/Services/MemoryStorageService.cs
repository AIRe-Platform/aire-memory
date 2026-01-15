// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Web.Http;
using Aire.Sdk.Azure;
using Aire.Sdk.Models.Platform;
using Aire.Sdk.Platform;

namespace Aire.Memory.Services;

public class MemoryStorageService(
    ITableStorageServiceFactory storageServiceFactory,
    IAirePlatformService platformService)
{
    private readonly string selfId = AireEnvironment.ModuleIdentifier
        ?? throw new AirePlatformException("Missing module identifier");

    public async Task<ITableStorageService> GetTableStorageService(string platformId, string? targetId)
    {
        bool matchExact = !selfId.EndsWith('*');
        if (matchExact)
        {
            targetId ??= selfId;
            if (selfId != targetId)
                throw new HttpResponseException(HttpStatusCode.Forbidden);
        }
        else
        {
            if (targetId == null)
                throw new HttpResponseException(HttpStatusCode.BadRequest);

            if (!targetId.StartsWith(selfId.TrimEnd('*')))
                throw new HttpResponseException(HttpStatusCode.Forbidden);
        }

        var module = await platformService.GetPlatformModule(platformId, ModuleType.Memory, targetId)
            ?? throw new HttpResponseException(HttpStatusCode.BadRequest);

        string prefix = "";
        if (module.Settings != null)
        {
            if (module.Settings.TryGetValue(ModuleSettings.Memory_TablePrefix, out dynamic? prefixSetting))
            {
                if (prefixSetting is string value)
                    prefix = value;
            }
        }

        return storageServiceFactory.Create(prefix);
    }
}
