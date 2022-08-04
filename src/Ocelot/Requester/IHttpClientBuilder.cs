namespace Ocelot.Requester
{
    using Ocelot.Configuration;
    using System.Net.Http;

    public interface IHttpClientBuilder
    {
        HttpClient Create(DownstreamRoute downstreamRoute);
    }
}
