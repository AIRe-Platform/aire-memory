// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using Aire.Sdk.Models.Platform;
using Aire.Sdk.Platform;
using Microsoft.Extensions.Options;

namespace Aire.Memory.Services;

public class ModuleConfig
{
    public string? ModuleIdentifier { get; set; }
}

public class ModuleConfigService(IAirePlatformService platformService, IOptions<ModuleConfig> options)
{
    public async Task<T?> Get<T>(string platform, string key)
    {
        var selfId = options.Value.ModuleIdentifier
            ?? throw new Exception("Invalid module config");

        var module = await platformService.GetPlatformModule(platform, ModuleType.Memory, selfId);
        if (module?.Settings == null)
            return default;

        if (module.Settings.TryGetValue(key, out dynamic? value))
        {
            if (value is T t)
                return t;
        }
        return default;
    }
}