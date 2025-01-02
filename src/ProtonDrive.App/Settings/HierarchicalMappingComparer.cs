﻿using System.Collections.Generic;

namespace ProtonDrive.App.Settings;

internal sealed class HierarchicalMappingComparer : IComparer<RemoteToLocalMapping>
{
    private readonly IDictionary<MappingType, int> _mappingTypeHierarchy = new Dictionary<MappingType, int>
    {
        { MappingType.HostDeviceFolder, 1 },
        { MappingType.CloudFiles, 2 },
        { MappingType.ForeignDevice, 3 },
        { MappingType.SharedWithMeRootFolder, 4 },
        { MappingType.SharedWithMeItem, 5 },
    };

    public static HierarchicalMappingComparer Instance { get; } = new();

    public int Compare(RemoteToLocalMapping? x, RemoteToLocalMapping? y)
    {
        if (x is null || y is null)
        {
            return 0;
        }

        if (x.Type != y.Type)
        {
            return _mappingTypeHierarchy[x.Type].CompareTo(_mappingTypeHierarchy[y.Type]);
        }

        return x.Id.CompareTo(y.Id);
    }
}
