using System;
using System.Collections.Generic;
using AWSRedrive.Models;
using RestSharp;
using RestSharp.Authenticators;
using Xunit;

namespace AWSRedrive.Tests.Unit
{
    public class HttpMessageProcessorTests
    {
        [Fact]
        public void VerifyGetRequestUrlConstruction()
        {
            var processor = new HttpMessageProcessor();
            
            var request = processor.CreateGetRequest("{\"a\":\"b\",\"c\":\"d\"}", new Uri("http://localhost/v"));

            Assert.Equal("/v", request.Resource);
            Assert.Equal(2, request.Parameters.Count);
            var parameter = request.Parameters.TryFind("a");
            Assert.NotNull(parameter);
            Assert.Equal("b", parameter.Value);
            parameter = request.Parameters.TryFind("c");
            Assert.NotNull(parameter);
            Assert.Equal("d", parameter.Value);
        }

        [Fact]
        public void VerifyBodyAddition()
        {
            var processor = new HttpMessageProcessor();

            var request =
                processor.CreatePostOrPutOrDeleteRequest("{}", new Uri("http://localhost/v"), new ConfigurationEntry());

            Assert.Equal("/v", request.Resource);
            Assert.Single(request.Parameters);
            var parameter = request.Parameters.TryFind("");
            Assert.NotNull(parameter);
            Assert.Equal("{}", parameter.Value);
            Assert.Equal(ParameterType.RequestBody, parameter.Type);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        public void VerifyCorrectHttpVerb(string verb)
        {
            var processor = new HttpMessageProcessor();

            var entry = new ConfigurationEntry();
            var method = Method.Post;

            switch (verb.ToLower())
            {
                case "get": 
                    entry.UseGet = true;
                    method = Method.Get;
                    break;
                case "put":
                    entry.UsePut = true;
                    method = Method.Put;
                    break;
                case "delete":
                    entry.UseDelete = true;
                    method = Method.Delete;
                    break;
            }

            var request = processor.CreateRequest("{}", new Uri("http://localhost/v"), entry);

            Assert.Equal(method, request.Method);
        }

        [Fact]
        public void VerifyUri()
        {
            var processor = new HttpMessageProcessor();

            var options =
                processor.CreateOptions(new Uri("https://localhost:1234/v"), new ConfigurationEntry());

            Assert.Equal("https://localhost:1234/", options.BaseUrl?.AbsoluteUri);
        }

        [Fact]
        public void VerifyTimeoutSet()
        {
            var processor = new HttpMessageProcessor();

            var options =
                processor.CreateOptions(new Uri("http://localhost/v"), new ConfigurationEntry { Timeout = 1234 });

            Assert.NotNull(options.Timeout);
            Assert.Equal(1234, options.Timeout.Value.TotalMilliseconds);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyIgnoreCertificateErrors(bool ignore)
        {
            var processor = new HttpMessageProcessor();

            var options =
                processor.CreateOptions(new Uri("http://localhost/v"), new ConfigurationEntry { IgnoreCertificateErrors = ignore });

            if (ignore)
            {
                Assert.NotNull(options.RemoteCertificateValidationCallback);
            }
            else
            {
                Assert.Null(options.RemoteCertificateValidationCallback);
            }
        }

        [Theory]
        [InlineData("Authorization", "auth")]
        [InlineData("x-api-key", "1234")]
        public void VerifyTokensSet(string header, string value)
        {
            var processor = new HttpMessageProcessor();

            var request = new RestRequest();
            var entry = new ConfigurationEntry();
            if (header == "x-api-key")
            {
                entry.AwsGatewayToken = value;
            }
            else
            {
                entry.AuthToken = value;
            }

            processor.AddAuthentication(null, request, entry);

            Assert.Single(request.Parameters);
            var parameter = request.Parameters.TryFind(header);
            Assert.NotNull(parameter);
            Assert.Equal(value, parameter.Value);
            Assert.Equal(ParameterType.HttpHeader, parameter.Type);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyAuthentication(bool useBasicAuth)
        {
            var processor = new HttpMessageProcessor();

            var options = processor.CreateOptions(new Uri("http://localhost"), new ConfigurationEntry
            {
                BasicAuthPassword = useBasicAuth ? "abc" : string.Empty,
                BasicAuthUserName = useBasicAuth ? "1234" : string.Empty
            });

            if (useBasicAuth)
            {
                Assert.NotNull(options.Authenticator);
                Assert.IsType<HttpBasicAuthenticator>(options.Authenticator);
            }
            else
            {
                Assert.Null(options.Authenticator);
            }
        }

        [Fact]
        public void VerifyAttributesSetAsHeaders()
        {
            var processor = new HttpMessageProcessor();

            var request = new RestRequest();
            processor.AddAttributes(request, null);

            Assert.Empty(request.Parameters);

            processor.AddAttributes(request, new Dictionary<string, string>());

            Assert.Empty(request.Parameters);

            processor.AddAttributes(request, new Dictionary<string, string> { { "a", "b" }, { "c", "d" } });

            Assert.Equal(2, request.Parameters.Count);
            var parameter = request.Parameters.TryFind("a");
            Assert.NotNull(parameter);
            Assert.Equal("b", parameter.Value);
            parameter = request.Parameters.TryFind("c");
            Assert.NotNull(parameter);
            Assert.Equal("d", parameter.Value);
        }

        [Fact]
        public void NotTryingToUnpackMessageToDetermineAttributes()
        {
            var processor = new HttpMessageProcessor();

            var request = new RestRequest();
            processor.UnpackAttributesAsHeaders("not a json", request,
                new ConfigurationEntry { UnpackAttributesAsHeaders = false });

            Assert.Empty(request.Parameters);
        }

        [Fact]
        public void VerifyNoErrorIfMessageIsNotSns()
        {
            var processor = new HttpMessageProcessor();

            var request = new RestRequest();
            processor.UnpackAttributesAsHeaders("not a json", request,
                new ConfigurationEntry { UnpackAttributesAsHeaders = true });
        }

        [Fact]
        public void VerifyUnpackAttributesAsHeaders()
        {
            var processor = new HttpMessageProcessor();

            var request = new RestRequest();
            processor.UnpackAttributesAsHeaders("{\"MessageAttributes\":{\"requestId\":{\"Value\":\"id\"},\"x-id\":{\"Value\":123}}}", request,
                new ConfigurationEntry { UnpackAttributesAsHeaders = true });

            Assert.Equal(2, request.Parameters.Count);

            var parameter = request.Parameters.TryFind("requestId");
            Assert.NotNull(parameter);
            Assert.Equal("id", parameter.Value);
            Assert.Equal(ParameterType.HttpHeader, parameter.Type);

            parameter = request.Parameters.TryFind("x-id");
            Assert.NotNull(parameter);
            Assert.Equal("123", parameter.Value);
            Assert.Equal(ParameterType.HttpHeader, parameter.Type);
        }
    }
}
