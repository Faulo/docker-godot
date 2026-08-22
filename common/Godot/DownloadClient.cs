using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace Godot;

interface IDownloadClient {
    string ReadText(string uri, bool github);

    void Save(string uri, string destination);
}

sealed class DownloadClient : IDownloadClient {
    const int MAX_ATTEMPTS = 5;

    static readonly HttpClient HttpClient = CreateHttpClient();

    public static readonly DownloadClient shared = new();

    DownloadClient() {
    }

    public string ReadText(string uri, bool github) {
        return Retry(uri, "request", () => {
            using var request = CreateRequest(uri, github);
            using var response = HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        });
    }

    public void Save(string uri, string destination) {
        Retry<object?>(uri, "download", () => {
            long offset = File.Exists(destination) ? new FileInfo(destination).Length : 0;
            using var request = CreateRequest(uri, false);
            if (offset > 0) {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            using var response = HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0) {
                File.Delete(destination);
                throw new IOException("server rejected download resume offset: " + uri);
            }

            response.EnsureSuccessStatusCode();
            bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (append && response.Content.Headers.ContentRange?.From != offset) {
                File.Delete(destination);
                throw new IOException("server resumed download at an unexpected offset: " + uri);
            }

            using var input = response.Content.ReadAsStream();
            using var output = new FileStream(destination, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
            input.CopyTo(output);
            return null;
        });
    }

    static HttpClient CreateHttpClient() {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    }

    static HttpRequestMessage CreateRequest(string uri, bool github) {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("docker-godot");
        if (github) {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
        }

        return request;
    }

    static T Retry<T>(string uri, string operation, Func<T> action) {
        Exception? last = null;
        for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
            try {
                return action();
            } catch (Exception exception) when (IsTransient(exception)) {
                last = exception;
                if (attempt < MAX_ATTEMPTS) {
                    if (operation == "download") {
                        Console.Out.WriteLine("docker-godot: download interrupted; resuming attempt " + (attempt + 1) + " of " + MAX_ATTEMPTS);
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(attempt * 2));
                }
            }
        }

        throw new IOException(operation + " failed after " + MAX_ATTEMPTS + " attempts: " + uri, last);
    }

    static bool IsTransient(Exception exception) {
        if (exception is IOException or OperationCanceledException) {
            return true;
        }

        if (exception is not HttpRequestException requestException) {
            return false;
        }

        return requestException.StatusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError;
    }
}