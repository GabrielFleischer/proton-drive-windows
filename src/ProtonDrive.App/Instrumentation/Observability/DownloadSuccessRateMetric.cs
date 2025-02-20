﻿using System;
using ProtonDrive.Client.Instrumentation.Observability;
using ProtonDrive.Shared.Extensions;

namespace ProtonDrive.App.Instrumentation.Observability;

public sealed record DownloadSuccessRateMetric : ObservabilityMetric
{
    public DownloadSuccessRateMetric(ObservabilityMetricProperties properties)
        : base("drive_download_success_rate_total", Version: 1, DateTime.UtcNow.ToUnixTimeSeconds(), properties)
    {
    }
}
