using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SmallSample.MyPages.Api.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using static SmallSample.MyPages.Api.Constants.ApiEndpointPath;

namespace SmallSample.MyPages.Api.Services;

public class ApiHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _hca;
    private readonly ApiSettings _apiSettings;

    public ApiHandler(IHttpContextAccessor hca, IOptions<ApiSettings> apiSettings)
    {
        _hca = hca;
        _apiSettings = apiSettings.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.UseBasicAuthentication(_apiSettings.UserName, _apiSettings.Password);

        var url = request.RequestUri?.ToString()
            ?? throw new ArgumentException("The request.RequestUri is null.", nameof(request));

        // Always insert the tenant's name.
        var replacedUrl = url.Replace(ClientNameSegment, _apiSettings.ClubId);

        // Only insert source system name and ID when necessary.
        if (url.Contains(SourceSystemNameSegment))
        {
            // Local path will be e.g. /v1/<clientName>/access/<sourceSystemName>;<sourceSystemId>.
            var endpoint = request.RequestUri.LocalPath.Split('/')[3];
            var endpointConfig = _apiSettings.ApiEndpointSettings?.GetConfig(endpoint)
                ?? throw new ArgumentException($"No configuration found for endpoint \"{endpoint}\".");
            var claimType = endpointConfig.UserClaim;
            var sourceSystemId = _hca.HttpContext?.User.Claims.FirstOrDefault(claim => claim.Type == claimType)?.Value
                ?? throw new ArgumentException($"Claim \"{claimType}\" not found for current user.");

            replacedUrl = replacedUrl
                .Replace(SourceSystemNameSegment, endpointConfig.SourceSystemName)
                .Replace(SourceSystemIdSegment, sourceSystemId);
        }

        request.RequestUri = new Uri(replacedUrl);

        return base.SendAsync(request, cancellationToken);
    }
}
