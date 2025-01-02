﻿using System.Collections.Generic;
using ProtonDrive.Client.Instrumentation.Telemetry;

namespace ProtonDrive.App.Instrumentation.Telemetry.FirstLaunch;

internal static class FirstLaunchReportFactory
{
    public static TelemetryEvent CreateEvent(string source)
    {
        const string measurementGroupName = "common.any.client_installs";
        const string eventName = "client_first_launch";

        return new TelemetryEvent(
            measurementGroupName,
            eventName,
            new Dictionary<string, double>(),
            new Dictionary<string, string>
            {
                { "client", "windows" },
                { "product", "drive" },
                { "install_source", source },
            });
    }
}
