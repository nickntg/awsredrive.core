using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AWSRedrive.Interfaces;
using AWSRedrive.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators;

namespace AWSRedrive
{
    public class HttpMessageProcessor : IMessageProcessor
    {
        private readonly string[] _ignoredHeaders = ["content-length", "host", "accept-encoding", "content-type", "accept"];

        public void ProcessMessage(string message, Dictionary<string, string> attributes, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            var uri = new Uri(configurationEntry.RedriveUrl);
            var method = configurationEntry.UseGET ? "GET" : configurationEntry.UsePUT ? "PUT" : configurationEntry.UseDelete ? "DELETE" : "POST";
            
            logger.Debug($"Preparing {method} request to {uri.Host}{uri.PathAndQuery}");
            logger.Trace($"Timeout: {configurationEntry.Timeout}ms, IgnoreCertErrors: {configurationEntry.IgnoreCertificateErrors}");

            var options = CreateOptions(uri, configurationEntry);
            var client = new RestClient(options);
            var request = CreateRequest(message, uri, configurationEntry);

            AddAuthentication(client, request, configurationEntry, logger);
            AddAttributes(request, attributes, logger);
            UnpackAttributesAsHeaders(message, request, configurationEntry, logger);

            logger.Trace($"Request body: {message?.Length ?? 0} chars");

            SendRequest(client, request, configurationEntry, logger);
        }

        public void UnpackAttributesAsHeaders(string message, RestRequest request, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            if (!configurationEntry.UnpackAttributesAsHeaders)
            {
                return;
            }

            try
            {
                var snsEnvelope = JsonConvert.DeserializeObject<SnsEnvelope>(message);
                if (snsEnvelope.MessageAttributes is null)
                {
                    return;
                }

                var count = 0;
                foreach (var attribute in snsEnvelope.MessageAttributes)
                {
                    var value = attribute.Value.Value.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (!_ignoredHeaders.Contains(attribute.Key.ToLower()))
                        {
                            request.AddHeader(attribute.Key, value);
                            count++;
                        }
                    }
                }
                
                if (count > 0)
                {
                    logger.Trace($"Unpacked {count} SNS message attributes as headers");
                }
            }
            catch
            {
                // Ignored - not an SNS envelope
            }
        }

        public RestRequest CreateRequest(string message, Uri uri, ConfigurationEntry configurationEntry)
        {
            return !configurationEntry.UseGET 
                ? CreatePostOrPutOrDeleteRequest(message, uri, configurationEntry) 
                : CreateGetRequest(message, uri);
        }

        public RestRequest CreateGetRequest(string message, Uri uri)
        {
            var request = new RestRequest(uri.PathAndQuery);
            try
            {
                var data = JObject.Parse(message);
                foreach (var p in data.Properties())
                {
                    request.AddQueryParameter(p.Name, p.Value.ToString());
                }
            }
            catch
            {
                // If parsing fails, just use the request without query parameters
            }

            return request;
        }

        public RestRequest CreatePostOrPutOrDeleteRequest(string message, Uri uri, ConfigurationEntry configurationEntry)
        {
            var request = new RestRequest(uri.PathAndQuery, configurationEntry.UseDelete ? Method.Delete : configurationEntry.UsePUT ? Method.Put : Method.Post);

            request.AddStringBody(message, DataFormat.Json);

            return request;
        }

        public RestClientOptions CreateOptions(Uri uri, ConfigurationEntry configurationEntry)
        {
            var options = new RestClientOptions($"{uri.Scheme}://{uri.Host}:{uri.Port}");

            if (configurationEntry.IgnoreCertificateErrors)
            {
                options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            if (configurationEntry.Timeout.HasValue)
            {
                options.Timeout = TimeSpan.FromMilliseconds(configurationEntry.Timeout.Value);
            }

            if (!string.IsNullOrEmpty(configurationEntry.BasicAuthPassword) &&
                !string.IsNullOrEmpty(configurationEntry.BasicAuthUserName))
            {
                options.Authenticator = new HttpBasicAuthenticator(configurationEntry.BasicAuthUserName,
                    configurationEntry.BasicAuthPassword);
            }

            return options;
        }

        public void AddAuthentication(RestClient client, RestRequest request, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            if (!string.IsNullOrEmpty(configurationEntry.AwsGatewayToken))
            {
                request.AddHeader("x-api-key", configurationEntry.AwsGatewayToken);
                logger.Trace("Added x-api-key header");
            }

            if (!string.IsNullOrEmpty(configurationEntry.AuthToken))
            {
                request.AddHeader("Authorization", configurationEntry.AuthToken);
                logger.Trace("Added Authorization header");
            }

            if (!string.IsNullOrEmpty(configurationEntry.BasicAuthUserName))
            {
                logger.Trace("Using Basic authentication");
            }
        }

        public void AddAttributes(RestRequest request, Dictionary<string, string> attributes, EntryLogger logger)
        {
            if (attributes == null || attributes.Count == 0)
            {
                return;
            }

            var count = 0;
            foreach (var key in attributes.Keys.Where(key => !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(attributes[key])))
            {
                if (!_ignoredHeaders.Contains(key.ToLower()))
                {
                    request.AddHeader(key, attributes[key]);
                    count++;
                }
            }

            if (count > 0)
            {
                logger.Trace($"Added {count} message attributes as headers");
            }
        }

        private void SendRequest(RestClient client, RestRequest request, ConfigurationEntry configurationEntry, EntryLogger logger)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = client.ExecuteAsync(request).Result;
            stopwatch.Stop();

            var elapsed = stopwatch.ElapsedMilliseconds;

            if (response.IsSuccessful)
            {
                logger.Debug($"Response: {(int)response.StatusCode} {response.StatusCode} ({elapsed}ms)");
                if (logger.IsTraceEnabled && !string.IsNullOrEmpty(response.Content))
                {
                    var preview = response.Content.Length > 500 
                        ? response.Content.Substring(0, 500) + "..." 
                        : response.Content;
                    logger.Trace($"Response body ({response.Content.Length} chars): {preview}");
                }
                return;
            }

            logger.Debug($"Request failed: {(int)response.StatusCode} {response.StatusCode} ({elapsed}ms)");
            if (logger.IsTraceEnabled && !string.IsNullOrEmpty(response.Content))
            {
                var preview = response.Content.Length > 500 
                    ? response.Content.Substring(0, 500) + "..." 
                    : response.Content;
                logger.Trace($"Error response ({response.Content.Length} chars): {preview}");
            }

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            throw new InvalidOperationException($"Received {response.StatusCode} status code with content [{response.Content}]");
        }
    }
}