﻿using System;
using System.Net;
using AWSRedrive.Interfaces;
using NLog;
using RestSharp;

namespace AWSRedrive
{
    public class HttpMessageProcessor : IMessageProcessor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void ProcessMessage(string message, ConfigurationEntry configurationEntry)
        {
            Logger.Trace($"Preparing post to {configurationEntry.RedriveUrl}");
            var uri = new Uri(configurationEntry.RedriveUrl);
            var client = new RestClient($"{uri.Scheme}://{uri.Host}");
            var post = new RestRequest(uri.PathAndQuery, Method.POST);
            post.AddBody(message);

            if (!string.IsNullOrEmpty(configurationEntry.AwsGatewayToken))
            {
                post.AddHeader("x-api-key", configurationEntry.AwsGatewayToken);
            }

            if (configurationEntry.Timeout.HasValue)
            {
                client.Timeout = configurationEntry.Timeout.Value;
            }

            Logger.Trace($"Posting to {configurationEntry.RedriveUrl}");
            var response = client.Execute(post);

            if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
            {
                Logger.Trace($"Post to {configurationEntry.RedriveUrl} successful");
                return;
            }

            Logger.Trace($"Post to {configurationEntry.RedriveUrl} failed (status code [{response.StatusCode}], error [{response.ErrorMessage}])");
            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            throw new InvalidOperationException($"Received {response.StatusCode} status code");
        }
    }
}