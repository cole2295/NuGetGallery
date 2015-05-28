// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NuGet.Services.Search.Client
{
    public class RetryingHttpClientWrapper
    {
        private readonly HttpClient _httpClient;
        private readonly Random _random = new Random((int)DateTime.UtcNow.Ticks);

        private static readonly TimeSpan FailedEndpointsCleanupInterval = TimeSpan.FromMinutes(1);
        private static DateTime _lastFailedEndpointsCleanup = DateTime.UtcNow;
        private static readonly object CleanupFailedEndpointsLock = new object();
        private static readonly Dictionary<string, DateTime> HostFailures = new Dictionary<string, DateTime>();

        public RetryingHttpClientWrapper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        private async Task<TResponseType> GetWithRetry<TResponseType>(IEnumerable<Uri> endpoints, Func<HttpClient, Uri, Task<TResponseType>> run)
        {
            CleanupFailedEndpoints();

            var exceptions = new List<Exception>();

            var healthyEndpoints = endpoints.Where(e => !HostFailures.ContainsKey(GetQueryLessUri(e))).OrderBy(r => _random.Next()).ToList();
            if (!healthyEndpoints.Any())
            {
                // No healthy endpoints are available in the current list of endpoints. Let's go with best-effort...
                healthyEndpoints = endpoints.OrderBy(r => _random.Next()).ToList();
            }
            foreach (var endpoint in healthyEndpoints)
            {
                try
                {
                    var response = await run(_httpClient, endpoint);

                    var responseMessage = response as HttpResponseMessage;
                    if (responseMessage != null && !responseMessage.IsSuccessStatusCode)
                    {
                        if (ShouldTryOther(responseMessage))
                        {
                            exceptions.Add(new HttpRequestException(responseMessage.ReasonPhrase));
                            MarkFailed(endpoint);
                            continue; // try another endpoint
                        }
                    }
                    
                    return response;
                }
                catch (Exception ex)
                {
                    if (ShouldTryOther(ex))
                    {
                        exceptions.Add(ex);
                        MarkFailed(endpoint);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            throw new AggregateException(exceptions);
        }

        private static string GetQueryLessUri(Uri uri)
        {
            var uriString = uri.ToString();
            var queryStart = uriString.IndexOf("?", StringComparison.Ordinal);
            if (queryStart >= 0)
            {
                return uriString.Substring(0, queryStart);
            }
            return uriString;
        }

        private static void CleanupFailedEndpoints()
        {
            if (_lastFailedEndpointsCleanup < DateTime.UtcNow - FailedEndpointsCleanupInterval)
            {
                lock (CleanupFailedEndpointsLock)
                {
                    if (_lastFailedEndpointsCleanup < DateTime.UtcNow - FailedEndpointsCleanupInterval)
                    {
                        _lastFailedEndpointsCleanup = DateTime.UtcNow;

                        var keys = HostFailures.Keys.ToArray();
                        foreach (var key in keys)
                        {
                            if (HostFailures[key] < DateTime.UtcNow - FailedEndpointsCleanupInterval)
                            {
                                HostFailures.Remove(key);
                            }
                        }
                    }
                }
            }
        }

        private static void MarkFailed(Uri endpoint)
        {
            HostFailures[GetQueryLessUri(endpoint)] = DateTime.UtcNow;
        }

        public async Task<string> GetStringAsync(IEnumerable<Uri> endpoints)
        {
            return await GetWithRetry(endpoints, (client, uri) => _httpClient.GetStringAsync(uri));
        }

        public async Task<HttpResponseMessage> GetAsync(IEnumerable<Uri> endpoints)
        {
            return await GetWithRetry(endpoints, (client, uri) => _httpClient.GetAsync(uri));
        }

        public bool ShouldTryOther(Exception ex)
        {
            var wex = ex as WebException;
            if (wex == null)
            {
                wex = ex.InnerException as WebException;
            }
            if (wex != null && (
                wex.Status == WebExceptionStatus.UnknownError
                || wex.Status == WebExceptionStatus.ConnectFailure
                || (int)wex.Status == 1 // NameResolutionFailure
                ))
            {
                return true;
            }

            if (ex is TaskCanceledException)
            {
                return true;
            }
            
            return false;
        }

        private bool ShouldTryOther(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode
                || response.StatusCode == HttpStatusCode.BadGateway
                || response.StatusCode == HttpStatusCode.GatewayTimeout
                || response.StatusCode == HttpStatusCode.ServiceUnavailable
                || response.StatusCode == HttpStatusCode.RequestTimeout
                || response.StatusCode == HttpStatusCode.InternalServerError)
            {
                return true;
            }

            return false;
        }
    }
}