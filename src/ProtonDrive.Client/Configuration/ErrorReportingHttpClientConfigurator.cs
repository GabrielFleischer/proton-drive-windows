﻿using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtonDrive.Shared.Localization;

namespace ProtonDrive.Client.Configuration;

internal sealed class ErrorReportingHttpClientConfigurator : IErrorReportingHttpClientConfigurator
{
    private readonly IServiceProvider _provider;

    public ErrorReportingHttpClientConfigurator(IServiceProvider provider)
    {
        _provider = provider;
    }

    public HttpMessageHandler CreateHttpMessageHandler()
    {
        var httpMessageHandler = new SocketsHttpHandler();
        HttpClientConfigurator.ConfigurePrimaryHttpMessageHandler(httpMessageHandler, ApiClientConfigurator.ErrorReportHttpClientName, _provider);
        return httpMessageHandler;
    }

    public void ConfigureHttpClient(HttpClient httpClient)
    {
        var config = _provider.GetRequiredService<DriveApiConfig>();
        var languageProvider = _provider.GetRequiredService<ILanguageProvider>();
        var culture = languageProvider.GetCulture();

        httpClient.DefaultRequestHeaders.AddApiRequestHeaders(config, culture);
    }
}
