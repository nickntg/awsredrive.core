﻿using System;
using System.Collections.Generic;
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
            Assert.NotNull(request.Parameters.TryFind("a"));
            Assert.Equal("b", request.Parameters.TryFind("a").Value);
            Assert.NotNull(request.Parameters.TryFind("c"));
            Assert.Equal("d", request.Parameters.TryFind("c").Value);
        }

        [Fact]
        public void VerifyBodyAddition()
        {
            var processor = new HttpMessageProcessor();

            var request =
                processor.CreatePostOrPutOrDeleteRequest("{}", new Uri("http://localhost/v"), new ConfigurationEntry());

            Assert.Equal("/v", request.Resource);
            Assert.Single(request.Parameters);
            Assert.NotNull(request.Parameters.TryFind(""));
            Assert.Equal("{}", request.Parameters.TryFind("").Value);
            Assert.Equal(ParameterType.RequestBody, request.Parameters.TryFind("").Type);
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
                    entry.UseGET = true;
                    method = Method.Get;
                    break;
                case "put":
                    entry.UsePUT = true;
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

            Assert.Equal(1234, options.MaxTimeout);
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
            Assert.NotNull(request.Parameters.TryFind(header));
            Assert.Equal(value, request.Parameters.TryFind(header).Value);
            Assert.Equal(ParameterType.HttpHeader, request.Parameters.TryFind(header).Type);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void VerifyAuthenticatorSet(bool useBasicAuth)
        {
            var processor = new HttpMessageProcessor();

            var client = new RestClient();
            processor.AddAuthentication(client, null,
                new ConfigurationEntry
                {
                    BasicAuthPassword = useBasicAuth ? "abc" : string.Empty,
                    BasicAuthUserName = useBasicAuth ? "1234" : string.Empty
                });

            if (useBasicAuth)
            {
                Assert.NotNull(client.Authenticator);
                Assert.IsType<HttpBasicAuthenticator>(client.Authenticator);
            }
            else
            {
                Assert.Null(client.Authenticator);
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
            Assert.NotNull(request.Parameters.TryFind("a"));
            Assert.Equal("b", request.Parameters.TryFind("a").Value);
            Assert.NotNull(request.Parameters.TryFind("c"));
            Assert.Equal("d", request.Parameters.TryFind("c").Value);
        }
    }
}
