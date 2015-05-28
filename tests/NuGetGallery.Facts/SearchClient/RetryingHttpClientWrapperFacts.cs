using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Services.Search.Client;
using Xunit;

namespace NuGetGallery.SearchClient
{
    public class RequestInspectingHandler 
        : DelegatingHandler
    {
        public HttpRequestMessage LastRequest { get; set; }

        public RequestInspectingHandler()
        {
            InnerHandler = new HttpClientHandler();
        }
    
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return base.SendAsync(request, cancellationToken);
        }
    }

    public class RetryingHttpClientWrapperFacts
    {
        private static readonly Uri ValidUri1 = new Uri("http://www.microsoft.com");
        private static readonly Uri ValidUri2 = new Uri("http://www.bing.com");
        private static readonly Uri InvalidUri1 = new Uri("http://nonexisting.domain.atleast.ihope");
        private static readonly Uri InvalidUri2 = new Uri("http://nonexisting.domain.atleast.ihope/foo");

        private RetryingHttpClientWrapper CreateWrapperClient(HttpMessageHandler handler)
        {
            return new RetryingHttpClientWrapper(new HttpClient(handler));
        }

        private RetryingHttpClientWrapper CreateWrapperClient()
        {
            return new RetryingHttpClientWrapper(new HttpClient());
        }

        [Fact]
        public void ReturnsStringForValidUri()
        {
            var client = CreateWrapperClient();

            var result = client.GetStringAsync(new[] { ValidUri1 }).Result;

            Assert.NotNull(result);
        }

        [Fact]
        public void ReturnsSuccessResponseForValidUri()
        {
            var client = CreateWrapperClient();

            var result = client.GetAsync(new[] { ValidUri1 }).Result;

            Assert.True(result.IsSuccessStatusCode);
        }

        [Fact]
        public void ReturnsStringForCollectionContainingValidUri()
        {
            var inspectingHandler = new RequestInspectingHandler();
            var client = CreateWrapperClient(inspectingHandler);

            for (int i = 0; i < 10; i++)
            {
                var result = client.GetStringAsync(new[] {InvalidUri1, InvalidUri2, ValidUri1}).Result;

                Assert.NotNull(result);
                Assert.True(inspectingHandler.LastRequest.RequestUri == ValidUri1);
            }
        }

        [Fact]
        public void ReturnsSuccessResponseForCollectionContainingValidUri()
        {
            var inspectingHandler = new RequestInspectingHandler();
            var client = CreateWrapperClient(inspectingHandler);

            for (int i = 0; i < 10; i++)
            {
                var result = client.GetAsync(new[] {InvalidUri1, InvalidUri2, ValidUri1}).Result;

                Assert.True(result.IsSuccessStatusCode);
                Assert.True(inspectingHandler.LastRequest.RequestUri == ValidUri1);
            }
        }

        [Fact]
        public void LoadBalancesBetweenValidUrisForGetStringAsync()
        {
            var inspectingHandler = new RequestInspectingHandler();
            var client = CreateWrapperClient(inspectingHandler);

            bool hasHitUri1 = false;
            bool hasHitUri2 = false;

            int numRequests = 0;
            while (!hasHitUri1 || !hasHitUri2 || numRequests < 25)
            {
                numRequests++;
                var result = client.GetStringAsync(new[] { ValidUri1, ValidUri2 }).Result;

                Assert.NotNull(result);
                if (!hasHitUri1) hasHitUri1 = inspectingHandler.LastRequest.RequestUri == ValidUri1;
                if (!hasHitUri2) hasHitUri2 = inspectingHandler.LastRequest.RequestUri == ValidUri2;
            }

            Assert.True(hasHitUri1, "The first valid Uri has not been hit within the limit of " + numRequests + " requests.");
            Assert.True(hasHitUri2, "The second valid Uri has not been hit within the limit of " + numRequests + " requests.");
        }

        [Fact]
        public void LoadBalancesBetweenValidUrisForGetAsync()
        {
            var inspectingHandler = new RequestInspectingHandler();
            var client = CreateWrapperClient(inspectingHandler);

            bool hasHitUri1 = false;
            bool hasHitUri2 = false;

            int numRequests = 0;
            while (!hasHitUri1 || !hasHitUri2 || numRequests < 25)
            {
                numRequests++;
                var result = client.GetAsync(new[] { ValidUri1, ValidUri2 }).Result;

                Assert.NotNull(result);
                if (!hasHitUri1) hasHitUri1 = inspectingHandler.LastRequest.RequestUri == ValidUri1;
                if (!hasHitUri2) hasHitUri2 = inspectingHandler.LastRequest.RequestUri == ValidUri2;
            }

            Assert.True(hasHitUri1, "The first valid Uri has not been hit within the limit of " + numRequests + " requests.");
            Assert.True(hasHitUri2, "The second valid Uri has not been hit within the limit of " + numRequests + " requests.");
        }

        [Fact]
        public void FailsWhenNoValidUriGiven1()
        {
            var client = CreateWrapperClient();

            Assert.Throws<AggregateException>(() => client.GetStringAsync(new[] { InvalidUri1 }).Result);
        }

        [Fact]
        public void FailsWhenNoValidUriGiven2()
        {
            var client = CreateWrapperClient();

            Assert.Throws<AggregateException>(() => client.GetAsync(new[] { InvalidUri1 }).Result);
        }
    }
}
