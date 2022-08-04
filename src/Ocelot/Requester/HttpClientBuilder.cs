namespace Ocelot.Requester
{
    using Ocelot.Configuration;
    using Ocelot.Logging;
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;

    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Options;

    public class HttpClientBuilder : IHttpClientBuilder
    {
        private readonly IDelegatingHandlerHandlerFactory _factory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOcelotLogger _logger;
        private DownstreamRoute _cacheKey;
        private HttpClient _httpClient;
        private readonly TimeSpan _defaultTimeout;

        public HttpClientBuilder(
            IDelegatingHandlerHandlerFactory factory,
            IHttpClientFactory httpClientFactory,
            IOcelotLogger logger)
        {
            _factory = factory;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            // This is hardcoded at the moment but can easily be added to configuration
            // if required by a user request.
            _defaultTimeout = TimeSpan.FromSeconds(90);
        }
        public string Name { get; private set; } = Options.DefaultName;
        public IServiceCollection Services { get; }

        public HttpClient Create(DownstreamRoute downstreamRoute)
        {
            var handler = CreateHandler(downstreamRoute);

            if (downstreamRoute.DangerousAcceptAnyServerCertificateValidator)
            {
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) => true;

                _logger
                    .LogWarning($"You have ignored all SSL warnings by using DangerousAcceptAnyServerCertificateValidator for this DownstreamRoute, UpstreamPathTemplate: {downstreamRoute.UpstreamPathTemplate}, DownstreamPathTemplate: {downstreamRoute.DownstreamPathTemplate}");
            }

            var timeout = downstreamRoute.QosOptions.TimeoutValue == 0
                ? _defaultTimeout
                : TimeSpan.FromMilliseconds(downstreamRoute.QosOptions.TimeoutValue);

            if (!string.IsNullOrEmpty(downstreamRoute.Key))
            {
                Name = downstreamRoute.Key;
            }

            if (!string.IsNullOrEmpty(downstreamRoute.ServiceName))
            {
                Name = downstreamRoute.ServiceName;
            }

            //TODO: Definir donde se anexaran los handlers
            //services.Configure<HttpClientFactoryOptions>(
            //    Name,
            //    options => options.HttpMessageHandlerBuilderActions.Add(builder =>
            //    {
            //        builder.PrimaryHandler = CreateHttpMessageHandler(handler, downstreamRoute);
            //        builder.Build();
            //    }));

            _httpClient = _httpClientFactory.CreateClient(Name);
            _httpClient.Timeout = timeout;

            return _httpClient;
        }

        private HttpClientHandler CreateHandler(DownstreamRoute downstreamRoute)
        {
            // Dont' create the CookieContainer if UseCookies is not set or the HttpClient will complain
            // under .Net Full Framework
            var useCookies = downstreamRoute.HttpHandlerOptions.UseCookieContainer;

            return useCookies ? UseCookiesHandler(downstreamRoute) : UseNonCookiesHandler(downstreamRoute);
        }

        private HttpClientHandler UseNonCookiesHandler(DownstreamRoute downstreamRoute)
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = downstreamRoute.HttpHandlerOptions.AllowAutoRedirect,
                UseCookies = downstreamRoute.HttpHandlerOptions.UseCookieContainer,
                UseProxy = downstreamRoute.HttpHandlerOptions.UseProxy,
                MaxConnectionsPerServer = downstreamRoute.HttpHandlerOptions.MaxConnectionsPerServer,
            };
        }

        private HttpClientHandler UseCookiesHandler(DownstreamRoute downstreamRoute)
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = downstreamRoute.HttpHandlerOptions.AllowAutoRedirect,
                UseCookies = downstreamRoute.HttpHandlerOptions.UseCookieContainer,
                UseProxy = downstreamRoute.HttpHandlerOptions.UseProxy,
                MaxConnectionsPerServer = downstreamRoute.HttpHandlerOptions.MaxConnectionsPerServer,
                CookieContainer = new CookieContainer(),
            };
        }

        private HttpMessageHandler CreateHttpMessageHandler(HttpMessageHandler httpMessageHandler, DownstreamRoute request)
        {
            //todo handle error
            var handlers = _factory.Get(request).Data;

            handlers
                .Select(handler => handler)
                .Reverse()
                .ToList()
                .ForEach(handler =>
                {
                    var delegatingHandler = handler();
                    delegatingHandler.InnerHandler = httpMessageHandler;
                    httpMessageHandler = delegatingHandler;
                });
            return httpMessageHandler;
        }
    }
}
