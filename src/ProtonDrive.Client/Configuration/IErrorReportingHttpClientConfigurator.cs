﻿using System.Net.Http;

namespace ProtonDrive.Client.Configuration;

public interface IErrorReportingHttpClientConfigurator
{
    HttpMessageHandler CreateHttpMessageHandler();

    void ConfigureHttpClient(HttpClient httpClient);
}
